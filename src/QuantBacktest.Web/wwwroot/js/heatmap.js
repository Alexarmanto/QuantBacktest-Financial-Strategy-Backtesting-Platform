// Optimisation heatmap (P4-X2, O1 to O5). Every value comes prepared by HeatmapViewModel in #optimize-heatmap-data:
// the cell values of the contract (never recomputed), every text already formatted, the best cell, the cell URLs and
// the side and rank of each cell for its colour. This module only draws: colour mixing, patterns, tooltip, click.
// Chart.js and chartjs-chart-matrix are UMD builds loaded just before this module; the matrix plugin registers itself
// (chartjs-chart-matrix.min.js line 7: e.Chart.register(r,u)), so there is no Chart.register here.
// Colours and tile sizes: the --qb-heat-* variables of site.css, read through readCssVariables (charts.js).
import { readCssVariables, readTheme } from './charts.js';

const variables = [
    '--qb-heat-low', '--qb-heat-mid', '--qb-heat-high', '--qb-heat-panel', '--qb-heat-outline', '--qb-heat-null',
    '--qb-heat-null-stripe', '--qb-heat-dot', '--qb-heat-best', '--qb-heat-best-inner', '--qb-heat-dim-mix',
    '--qb-heat-hatch-tile', '--qb-heat-dot-tile', '--qb-heat-min-share',
];

// Width of the white ring of the best cell; the dark inner line sits just inside it.
const bestRing = 3;

// Longest line of the tooltip note, in characters: fits a 360 px screen.
const noteLineLength = 36;

const source = document.getElementById('optimize-heatmap-data');
const canvas = document.getElementById('chart-heatmap');
if (source && canvas) {
    init(JSON.parse(source.textContent), canvas);
}

function init(payload, canvas) {
    const columns = payload.xLabels.length;
    const rows = payload.yLabels.length;
    const state = { colours: null, patterns: null, theme: null };

    // The patterns are built once, and again only when the theme changes (never one pattern per cell).
    function readColours() {
        const vars = readCssVariables(variables);
        state.theme = readTheme();
        state.colours = {
            low: rgb(vars['--qb-heat-low']),
            mid: rgb(vars['--qb-heat-mid']),
            high: rgb(vars['--qb-heat-high']),
            panel: rgb(vars['--qb-heat-panel']),
            outline: vars['--qb-heat-outline'],
            best: vars['--qb-heat-best'],
            bestInner: vars['--qb-heat-best-inner'],
            dimMix: Number(vars['--qb-heat-dim-mix']),
            minShare: Number(vars['--qb-heat-min-share']),
        };
        const context = canvas.getContext('2d');
        state.patterns = {
            hatch: hatchPattern(context, vars['--qb-heat-null'], vars['--qb-heat-null-stripe'], Number(vars['--qb-heat-hatch-tile'])),
            dots: dotPattern(context, vars['--qb-heat-dot'], Number(vars['--qb-heat-dot-tile'])),
        };
    }

    readColours();

    const point = cell => ({ x: payload.xLabels[cell.xi], y: payload.yLabels[cell.yi], cell });
    const cellWidth = ({ chart }) => (chart.chartArea ? chart.chartArea.width / columns : 0);
    const cellHeight = ({ chart }) => (chart.chartArea ? chart.chartArea.height / rows : 0);
    const inset = size => context => Math.max(0, size(context) - (2 * bestRing));

    function fill(context) {
        const cell = context.raw?.cell;
        if (!cell) {
            return 'transparent';
        }

        if (cell.value === null) {
            return state.patterns.hatch;
        }

        const colour = rankColour(cell, state.colours);
        return css(cell.dim ? mix(colour, state.colours.panel, state.colours.dimMix) : colour);
    }

    const datasets = [
        // Every cell: its colour (or the hatch when it has no value), 1 px outline so a centre cell stays visible.
        {
            data: payload.cells.map(point),
            backgroundColor: fill,
            borderColor: () => state.colours.outline,
            borderWidth: 1,
            width: cellWidth,
            height: cellHeight,
            order: 4,
        },
        // Fewer than 5 trades: a dot screen over the mixed colour (a different shape from the hatch).
        {
            data: payload.cells.filter(cell => cell.dim).map(point),
            backgroundColor: () => state.patterns.dots,
            borderWidth: 0,
            width: cellWidth,
            height: cellHeight,
            order: 3,
        },
        // Best cell (SweepResult.Best): 3 px white ring, then a 1 px dark line inside it.
        {
            data: payload.cells.filter(cell => cell.best).map(point),
            backgroundColor: 'transparent',
            borderColor: () => state.colours.best,
            borderWidth: bestRing,
            width: cellWidth,
            height: cellHeight,
            order: 2,
        },
        {
            data: payload.cells.filter(cell => cell.best).map(point),
            backgroundColor: 'transparent',
            borderColor: () => state.colours.bestInner,
            borderWidth: 1,
            width: inset(cellWidth),
            height: inset(cellHeight),
            order: 1,
        },
    ];

    let touched = null;
    const chart = new Chart(canvas, {
        type: 'matrix',
        data: { datasets },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false,
            interaction: { mode: 'point', intersect: true },
            plugins: {
                legend: { display: false },
                tooltip: {
                    filter: item => item.datasetIndex === 0,
                    callbacks: {
                        title: items => (items.length > 0 ? title(payload, items[0].raw.cell) : ''),
                        label: item => `${payload.metricLabel}: ${item.raw.cell.valueText}`,
                        afterLabel: item => lines(payload, item.raw.cell),
                    },
                },
            },
            scales: {
                x: axis(payload.xLabels, payload.xLabel, state.theme, { autoSkip: true, autoSkipPadding: 8, maxRotation: 0 }),
                // Smallest Y at the bottom, as on a chart.
                y: { ...axis(payload.yLabels, payload.yLabel, state.theme, { autoSkip: false }), reverse: true },
            },
            onHover: (event, elements) => {
                canvas.style.cursor = baseCell(payload, elements)?.url ? 'pointer' : 'default';
            },
            // Mouse or pen: a click opens the backtest of the cell. Touch: the first tap shows the tooltip, a second
            // tap on the same cell opens it (decision g-7). Keyboard users have the links of the table.
            onClick: (event, elements) => {
                const cell = baseCell(payload, elements);
                if (!cell?.url) {
                    return;
                }

                if (event.native?.pointerType === 'touch' && touched !== cell) {
                    touched = cell;
                    return;
                }

                window.location.assign(cell.url);
            },
        },
    });

    // Theme switch (data-bs-theme): read the colours again, rebuild the two patterns, redraw.
    new MutationObserver(() => {
        readColours();
        for (const scale of [chart.options.scales.x, chart.options.scales.y]) {
            scale.ticks.color = state.theme.muted;
            scale.title.color = state.theme.muted;
            scale.border.color = state.theme.grid;
        }

        chart.update('none');
    }).observe(document.documentElement, { attributes: true, attributeFilter: ['data-bs-theme'] });
}

// The cell under the pointer, read from the base dataset (the overlays repeat some of its cells).
function baseCell(payload, elements) {
    const element = elements.find(e => e.datasetIndex === 0);
    return element ? payload.cells[element.index] : null;
}

// One line per parameter, so the title stays inside a 360 px frame. Values as formatted by the server.
function title(payload, cell) {
    return [`${payload.xLabel} = ${cell.xText}`, `${payload.yLabel} = ${cell.yText}`];
}

function lines(payload, cell) {
    const result = [`${payload.tradesLabel}: ${cell.tradesText}`];
    if (cell.note) {
        result.push(...wrap(cell.note, noteLineLength));
    }

    return result;
}

// Chart.js never wraps a tooltip line: the note (an error message can be long) is cut on spaces into lines of at most
// `length` characters. Layout only, the text is unchanged; a single word longer than `length` keeps its own line.
function wrap(text, length) {
    const result = [];
    let current = '';
    for (const word of text.split(' ')) {
        if (current === '') {
            current = word;
        } else if (current.length + 1 + word.length <= length) {
            current += ` ${word}`;
        } else {
            result.push(current);
            current = word;
        }
    }

    if (current !== '') {
        result.push(current);
    }

    return result;
}

function axis(labels, text, theme, ticks) {
    return {
        type: 'category',
        labels,
        offset: true,
        grid: { display: false },
        border: { color: theme.grid },
        ticks: { color: theme.muted, ...ticks },
        title: { display: true, text, color: theme.muted },
    };
}

// Diverging colour by rank (HeatmapViewModel.Ranks): side +1 mixes towards the high colour, side -1 towards the low
// colour, by t (0 = closest to the centre, 1 = farthest), never below the minimum share so the closest cell stays
// apart from the centre colour. Side 0 (equal to the centre) is the centre colour.
function rankColour(cell, colours) {
    if (cell.side === 0 || cell.side === null) {
        return colours.mid;
    }

    const share = colours.minShare + ((1 - colours.minShare) * cell.t);
    return mix(cell.side > 0 ? colours.high : colours.low, colours.mid, share);
}

// share of a in the result, the rest is b.
function mix(a, b, share) {
    return a.map((channel, i) => Math.round((share * channel) + ((1 - share) * b[i])));
}

function rgb(hex) {
    const value = hex.replace('#', '');
    return [0, 2, 4].map(i => parseInt(value.slice(i, i + 2), 16));
}

function css(channels) {
    return `rgb(${channels[0]}, ${channels[1]}, ${channels[2]})`;
}

// "No value": diagonal stripes on the grey, tile of `size` px (<= 6), seamless when repeated.
function hatchPattern(context, background, stripe, size) {
    const tile = document.createElement('canvas');
    tile.width = size;
    tile.height = size;
    const g = tile.getContext('2d');
    g.fillStyle = background;
    g.fillRect(0, 0, size, size);
    g.strokeStyle = stripe;
    g.lineWidth = 1.5;
    g.beginPath();
    for (const offset of [-size, 0, size]) {
        g.moveTo(offset, size);
        g.lineTo(offset + size, 0);
    }

    g.stroke();
    return context.createPattern(tile, 'repeat');
}

// "Fewer than 5 trades": one dot per tile of `size` px (<= 6) on a transparent background, laid over the cell colour.
function dotPattern(context, dot, size) {
    const tile = document.createElement('canvas');
    tile.width = size;
    tile.height = size;
    const g = tile.getContext('2d');
    g.fillStyle = dot;
    g.beginPath();
    g.arc(size / 2, size / 2, 0.9, 0, 2 * Math.PI);
    g.fill();
    return context.createPattern(tile, 'repeat');
}
