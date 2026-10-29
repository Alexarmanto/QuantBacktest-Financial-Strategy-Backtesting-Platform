// Result charts (P4-U5): equity, drawdown, price with indicators and trade markers, oscillators. Every value comes
// prepared by ResultChartsViewModel in #result-charts-data (drawdowns already negative and in percent, reference lines
// deduplicated, markers paired with their date and fill price): this module only draws and formats, no finance.
// Chart.js is the UMD build loaded just before this module (globalThis.Chart).

// Formats of DisplayFormat (P4-U14), en-US: $10,120.50, -$10.50, -23%, -1.39%. A value rounded to zero has no sign.
const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', signDisplay: 'negative' });
const percentTick = new Intl.NumberFormat('en-US', { maximumFractionDigits: 2, signDisplay: 'negative' });
const percentValue = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2, signDisplay: 'negative' });
// Oscillators mix scales (RSI 54.32, ROC fraction 0.01234, MACD 1.235): significant digits, not fixed decimals.
const number = new Intl.NumberFormat('en-US', { maximumSignificantDigits: 4, signDisplay: 'negative' });

const formatMoney = value => money.format(value);
const formatPercentTick = value => `${percentTick.format(value)}%`;
const formatPercent = value => `${percentValue.format(value)}%`;
const formatNumber = value => number.format(value);

// Date axis: about one tick per 90 px of chart width, between 3 and 8, so labels never overlap (360 px to full width).
const pixelsPerTick = 90;
const minTicks = 3;
const maxTicks = 8;
const tickLimit = width => Math.min(maxTicks, Math.max(minTicks, Math.floor(width / pixelsPerTick)));

const source = document.getElementById('result-charts-data');
if (source) {
    init(JSON.parse(source.textContent));
}

function init(payload) {
    const theme = readTheme();
    const dates = payload.dates;
    theme.shortDates = spansMoreThanTwoYears(dates);

    const equityCanvas = document.getElementById('chart-equity');
    const equity = new Chart(equityCanvas, {
        type: 'line',
        data: {
            labels: dates,
            datasets: [
                line(payload.strategyLabel, payload.equity.strategy, theme.strategy),
                line(payload.benchmarkLabel, payload.equity.benchmark, theme.benchmark, { borderDash: [6, 4] }),
            ],
        },
        options: options(theme, equityCanvas, formatMoney, formatMoney),
    });
    initLogToggle(equity);

    const drawdownCanvas = document.getElementById('chart-drawdown');
    new Chart(drawdownCanvas, {
        type: 'line',
        data: {
            labels: dates,
            datasets: [
                line(payload.strategyLabel, payload.drawdown.strategy, theme.strategy,
                    { fill: 'origin', backgroundColor: theme.strategyFill }),
                line(payload.benchmarkLabel, payload.drawdown.benchmark, theme.benchmark,
                    { fill: 'origin', backgroundColor: theme.benchmarkFill, borderDash: [6, 4] }),
            ],
        },
        options: options(theme, drawdownCanvas, formatPercent, formatPercentTick, { max: 0 }),
    });

    priceChart(theme, dates, payload.price);

    if (payload.oscillator) {
        oscillatorChart(theme, dates, payload.oscillator);
    }
}

function priceChart(theme, dates, price) {
    // Markers are placed on the index of their date in dense arrays (null elsewhere): the 'index' tooltip reads the
    // same index in every dataset. Their texts are listed per date, so two fills on one bar both show in the tooltip.
    const indexOfDate = new Map(dates.map((date, index) => [date, index]));
    const notes = new Map();
    const placed = { buy: new Array(dates.length).fill(null), sell: new Array(dates.length).fill(null) };
    for (const marker of price.markers) {
        const index = indexOfDate.get(marker.date);
        if (index === undefined) {
            continue;
        }

        if (placed[marker.side][index] === null) {
            placed[marker.side][index] = marker.price;
        }

        notes.set(marker.date, [...(notes.get(marker.date) ?? []), marker.text]);
    }

    const datasets = [
        line(price.closeLabel, price.close, theme.price),
        ...price.indicators.map((indicator, i) => line(indicator.label, indicator.values, theme.palette[i % theme.palette.length])),
    ];
    if (price.markers.length > 0) {
        datasets.push(
            marker(price.buyLabel, placed.buy, theme.gain, 0),
            marker(price.sellLabel, placed.sell, theme.loss, 180));
    }

    const canvas = document.getElementById('chart-price');
    const chartOptions = options(theme, canvas, formatMoney, formatMoney);
    chartOptions.plugins.tooltip.callbacks.afterBody = items => notes.get(dates[items[0]?.dataIndex]) ?? [];

    new Chart(canvas, { type: 'line', data: { labels: dates, datasets }, options: chartOptions });
}

function oscillatorChart(theme, dates, oscillator) {
    const datasets = oscillator.indicators.map((indicator, i) =>
        line(indicator.label, indicator.values, theme.palette[i % theme.palette.length]));

    // Horizontal guides (RSI 30 and 70, MACD 0...): constant dotted lines, out of the legend and the tooltip.
    for (const level of oscillator.referenceLines) {
        datasets.push(line(formatNumber(level), dates.map(() => level), theme.muted,
            { borderDash: [2, 4], borderWidth: 1, pointHoverRadius: 0, qbGuide: true }));
    }

    const canvas = document.getElementById('chart-oscillator');
    new Chart(canvas, {
        type: 'line',
        data: { labels: dates, datasets },
        options: options(theme, canvas, formatNumber, formatNumber),
    });
}

function initLogToggle(chart) {
    const button = document.querySelector('[data-chart-log-toggle]');
    if (!button) {
        return;
    }

    button.addEventListener('click', () => {
        const log = button.getAttribute('aria-pressed') !== 'true';
        button.setAttribute('aria-pressed', String(log));
        button.classList.toggle('active', log);
        chart.options.scales.y.type = log ? 'logarithmic' : 'linear';
        chart.update('none');
    });
}

function line(label, data, color, extra = {}) {
    return {
        label,
        data,
        borderColor: color,
        backgroundColor: color,
        borderWidth: 1.5,
        pointRadius: 0,
        pointHoverRadius: 3,
        ...extra,
    };
}

// Triangles at the fill prices: an explicit radius, the lines draw no points (pointRadius 0).
function marker(label, data, color, rotation) {
    return {
        label,
        data,
        showLine: false,
        pointStyle: 'triangle',
        rotation,
        pointRadius: 6,
        pointHoverRadius: 8,
        borderColor: color,
        backgroundColor: color,
        qbMarker: true,
    };
}

// Dates are ISO strings (yyyy-MM-dd): compared and cut as text, no Date object, so no time zone shift.
function spansMoreThanTwoYears(dates) {
    const first = dates[0];
    const twoYearsLater = `${Number(first.slice(0, 4)) + 2}${first.slice(4)}`;
    return dates[dates.length - 1] > twoYearsLater;
}

function options(theme, canvas, formatValue, formatTick, yBounds = {}) {
    return {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        normalized: true,
        interaction: { mode: 'index', intersect: false },
        // Called before the resize layout update, so the new limit applies to this render.
        onResize: (chart, size) => { chart.options.scales.x.ticks.maxTicksLimit = tickLimit(size.width); },
        plugins: {
            legend: {
                position: 'bottom',
                labels: {
                    color: theme.text,
                    boxWidth: 12,
                    boxHeight: 12,
                    padding: 8,
                    filter: (item, data) => !data.datasets[item.datasetIndex].qbGuide,
                },
            },
            tooltip: {
                // Marker values are fill prices: their text (with the exit reason) comes in afterBody instead.
                filter: item => !item.dataset.qbGuide && !item.dataset.qbMarker,
                callbacks: {
                    // The axis may show yyyy-MM; the tooltip always gives the full bar date.
                    title: items => (items.length > 0 ? items[0].label : ''),
                    label: item => `${item.dataset.label}: ${formatValue(item.parsed.y)}`,
                },
            },
        },
        scales: {
            x: {
                type: 'category',
                ticks: {
                    color: theme.muted,
                    autoSkip: true,
                    autoSkipPadding: 12,
                    maxRotation: 0,
                    maxTicksLimit: tickLimit(canvas.parentElement.clientWidth),
                    callback(value) {
                        const date = this.getLabelForValue(value);
                        return theme.shortDates ? date.slice(0, 7) : date;
                    },
                },
                grid: { color: theme.grid },
            },
            y: {
                type: 'linear',
                ...yBounds,
                ticks: { color: theme.muted, callback: value => formatTick(value) },
                grid: { color: theme.grid },
            },
        },
    };
}

function readTheme() {
    const style = getComputedStyle(document.documentElement);
    const read = name => style.getPropertyValue(name).trim();
    return {
        text: read('--bs-body-color'),
        muted: read('--bs-secondary-color'),
        grid: read('--bs-border-color'),
        gain: read('--qb-gain'),
        loss: read('--qb-loss'),
        strategy: read('--qb-series-strategy'),
        strategyFill: read('--qb-series-strategy-fill'),
        benchmark: read('--qb-series-benchmark'),
        benchmarkFill: read('--qb-series-benchmark-fill'),
        price: read('--qb-series-price'),
        palette: [1, 2, 3, 4, 5].map(i => read(`--qb-series-${i}`)),
    };
}
