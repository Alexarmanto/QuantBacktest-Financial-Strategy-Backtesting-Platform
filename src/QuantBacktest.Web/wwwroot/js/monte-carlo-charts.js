// Monte Carlo histograms (P4-X4, MC6). Bins, counts, range labels and the index of the bin holding the actual value
// all come prepared by MonteCarloChartsViewModel in #monte-carlo-data: this module only draws, no counting, no finance.
// Chart.js is the UMD build loaded just before this module (globalThis.Chart). Theme and options: charts.js.
import { options, readTheme } from './charts.js';

const count = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 });
const formatCount = value => count.format(value);

const source = document.getElementById('monte-carlo-data');
if (source) {
    const payload = JSON.parse(source.textContent);
    const theme = readTheme();
    for (const histogram of [payload.finalReturn, payload.maxDrawdown]) {
        draw(theme, payload, histogram);
    }
}

function draw(theme, payload, histogram) {
    const canvas = document.getElementById(histogram.canvasId);
    if (!canvas) {
        return;
    }

    // Two overlapping datasets on the same bins: every bin, and the bin of the actual value in another colour.
    const actualIndex = histogram.actualIndex;
    const datasets = [
        bar(payload.countLabel, histogram.counts.map((value, i) => (i === actualIndex ? null : value)), theme.strategy, theme),
    ];
    if (actualIndex !== null) {
        datasets.push(bar(payload.actualLabel, histogram.counts.map((value, i) => (i === actualIndex ? value : null)), theme.palette[0], theme));
    }

    const chartOptions = options(theme, canvas, formatCount, formatCount, { beginAtZero: true });
    // Tick = lower bound of the bin (the X axis title says so); the tooltip gives the full range.
    chartOptions.scales.x.ticks.callback = value => histogram.ticks[value];
    chartOptions.scales.x.title = axisTitle(histogram.xAxisTitle, theme);
    chartOptions.scales.y.ticks.precision = 0;
    chartOptions.scales.y.title = axisTitle(payload.yAxisTitle, theme);
    // Above the chart, left-aligned: Chart.js wraps the items on several rows when the width is short (360 px).
    chartOptions.plugins.legend.position = 'top';
    chartOptions.plugins.legend.align = 'start';
    const tooltip = chartOptions.plugins.tooltip;
    tooltip.filter = item => item.parsed.y !== null;
    tooltip.callbacks.title = items => (items.length > 0 ? histogram.ranges[items[0].dataIndex] : '');
    tooltip.callbacks.label = item => `${payload.countLabel}: ${formatCount(item.parsed.y)}`;
    tooltip.callbacks.afterBody = items => (items[0]?.dataIndex === actualIndex ? [histogram.actualText] : []);

    new Chart(canvas, {
        type: 'bar',
        data: { labels: histogram.ranges, datasets },
        options: chartOptions,
    });
}

function axisTitle(text, theme) {
    return { display: true, text, color: theme.muted };
}

// Full-width bars (a histogram has no gap between bins), separated by a thin border.
function bar(label, data, color, theme) {
    return {
        label,
        data,
        backgroundColor: color,
        borderColor: theme.grid,
        borderWidth: 1,
        grouped: false,
        barPercentage: 1,
        categoryPercentage: 1,
    };
}
