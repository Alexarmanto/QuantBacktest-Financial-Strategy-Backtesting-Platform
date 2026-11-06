// Train/test page (P4-X3). Every value comes prepared in #validate-charts-data (ValidateChartsViewModel): the training
// heatmap, exactly the payload of the heatmap page, drawn by heatmap.js; then one equity chart per period (the curves
// of BacktestResult.Equity, buy & hold dashed as on the result page). Without a best cell, periods is empty.
// This module only draws: no value is computed. Chart.js and the matrix plugin are UMD builds loaded just before.
import { formatMoney, line, options, readTheme, spansMoreThanTwoYears } from './charts.js';
import { drawHeatmap } from './heatmap.js';

const source = document.getElementById('validate-charts-data');
if (source) {
    const payload = JSON.parse(source.textContent);
    const canvas = document.getElementById('chart-heatmap');
    if (canvas) {
        drawHeatmap(payload.heatmap, canvas);
    }

    for (const period of payload.periods) {
        equity(payload, period);
    }
}

function equity(payload, period) {
    const canvas = document.getElementById(period.canvasId);
    if (!canvas || period.dates.length === 0) {
        return;
    }

    const theme = readTheme();
    theme.shortDates = spansMoreThanTwoYears(period.dates);
    new Chart(canvas, {
        type: 'line',
        data: {
            labels: period.dates,
            datasets: [
                line(payload.strategyLabel, period.strategy, theme.strategy),
                line(payload.benchmarkLabel, period.benchmark, theme.benchmark, { borderDash: [6, 4] }),
            ],
        },
        options: options(theme, canvas, formatMoney, formatMoney),
    });
}
