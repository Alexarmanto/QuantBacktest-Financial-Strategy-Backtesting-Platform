// Comparator chart (P4-X1, C1): one equity curve per strategy and the single buy & hold benchmark, dashed like on the
// result page. Every value comes prepared by CompareChartsViewModel in #compare-charts-data: this module only draws.
// Chart.js is the UMD build loaded just before this module (globalThis.Chart). Theme, axes and formats: charts.js.
import { formatMoney, line, options, readTheme, spansMoreThanTwoYears } from './charts.js';

const source = document.getElementById('compare-charts-data');
if (source) {
    draw(JSON.parse(source.textContent));
}

function draw(payload) {
    const theme = readTheme();
    theme.shortDates = spansMoreThanTwoYears(payload.dates);

    // Color is the 1-based palette index of the curve, shared with the swatch of its row in the table.
    const datasets = payload.series.map(series => line(series.label, series.values, theme.comparison[series.color - 1]));
    datasets.push(line(payload.benchmarkLabel, payload.benchmark, theme.benchmark, { borderDash: [6, 4] }));

    const canvas = document.getElementById('chart-compare');
    new Chart(canvas, {
        type: 'line',
        data: { labels: payload.dates, datasets },
        options: options(theme, canvas, formatMoney, formatMoney),
    });
}
