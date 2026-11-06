// Heatmap form (P4-X2). The server renders a working form (selected strategy, its axes and ranges) and stays the
// authority on every rule: this module only refines it. Ranges, defaults and the cell limit come from data-* attributes.
// Shared settings behaviour (dates, ]min, max] ranges, one submission with a spinner): backtest-form.js.
// initSweepForm is exported for the train/test form (validate-form.js), which is the heatmap form plus one field.
import { initSettings } from './backtest-form.js';

// The epsilon of O1 and of ParameterSweepService.BuildAxis (Engine/ParameterSweepService.cs:162).
const axisEpsilon = 1e-9;

const form = document.getElementById('optimize-form');
if (form) {
    initSweepForm(form);
}

// Values of an axis, counted exactly as the service counts them: Math.Floor(((max - min) / step) + 1e-9) + 1.
export function axisValueCount(min, max, step) {
    return Math.floor(((max - min) / step) + axisEpsilon) + 1;
}

export function initSweepForm(form) {
    const strategy = form.elements.namedItem('strategy');
    const timeframe = form.elements.namedItem('tf');
    const maxCells = Number(form.dataset.maxCells);

    const activeFieldset = () =>
        [...form.querySelectorAll('[data-strategy-fields]')].find(f => f.dataset.strategyFields === strategy.value) ?? null;

    // Only the selected strategy's inputs are enabled: a disabled fieldset submits nothing.
    function syncStrategy() {
        for (const fieldset of form.querySelectorAll('[data-strategy-fields]')) {
            const selected = fieldset.dataset.strategyFields === strategy.value;
            fieldset.disabled = !selected;
            fieldset.hidden = !selected;
        }

        const option = strategy.selectedOptions[0];
        const supported = !option || option.dataset.timeframes.split(' ').includes(timeframe.value);
        strategy.setCustomValidity(supported ? '' : `This strategy only supports: ${option.dataset.timeframes}.`);
        syncParameters();
    }

    // A new parameter on an axis: its range takes the catalog bounds and the P4-X2 default step.
    function applyAxisDefaults(select) {
        const option = select.selectedOptions[0];
        const group = select.closest('[data-axis-group]');
        const axis = select.dataset.axis;
        const min = group.querySelector(`[data-range="${axis}min"]`);
        const max = group.querySelector(`[data-range="${axis}max"]`);
        const step = group.querySelector(`[data-range="${axis}step"]`);
        for (const input of [min, max]) {
            input.min = option.dataset.min;
            input.max = option.dataset.max;
            input.step = option.dataset.step;
        }

        step.step = option.dataset.step;
        min.value = option.dataset.min;
        max.value = option.dataset.max;
        step.value = option.dataset.defaultStep;
    }

    // The two swept parameters are replaced by every cell: their p.* input is disabled (not sent) and hidden.
    function syncParameters() {
        const fieldset = activeFieldset();
        if (!fieldset) {
            return;
        }

        const x = fieldset.querySelector('[data-axis="x"]');
        const y = fieldset.querySelector('[data-axis="y"]');
        for (const parameter of fieldset.querySelectorAll('[data-parameter-key]')) {
            const swept = parameter.dataset.parameterKey === x.value || parameter.dataset.parameterKey === y.value;
            parameter.disabled = swept;
            parameter.hidden = swept;
        }

        y.setCustomValidity(x.value === y.value ? 'The X and Y parameters must be different.' : '');
        syncCount(fieldset);
    }

    // Counter read by screen readers (aria-live); the browser blocks a grid above the limit. Informative only: the
    // server counts again and refuses the same grids.
    function syncCount(fieldset) {
        const counter = fieldset.querySelector('[data-cell-count]');
        const read = name => Number(fieldset.querySelector(`[data-range="${name}"]`).value);
        const yStep = fieldset.querySelector('[data-range="ystep"]');
        const [xMin, xMax, xStep, yMin, yMax, yStepValue] = ['xmin', 'xmax', 'xstep', 'ymin', 'ymax', 'ystep'].map(read);
        yStep.setCustomValidity('');
        if (!(xStep > 0) || !(yStepValue > 0) || xMin > xMax || yMin > yMax) {
            counter.textContent = '';
            return;
        }

        const xCount = axisValueCount(xMin, xMax, xStep);
        const yCount = axisValueCount(yMin, yMax, yStepValue);
        const cells = xCount * yCount;
        counter.textContent = `${cells} cells (${xCount} × ${yCount}, max ${maxCells})`;
        if (cells > maxCells) {
            yStep.setCustomValidity(`The heatmap would have ${cells} cells; the maximum is ${maxCells}. Increase a step or narrow a range.`);
        }
    }

    strategy.addEventListener('change', syncStrategy);
    timeframe.addEventListener('change', syncStrategy);
    for (const select of form.querySelectorAll('[data-axis]')) {
        select.addEventListener('change', () => {
            applyAxisDefaults(select);
            syncParameters();
        });
    }

    for (const input of form.querySelectorAll('[data-range]')) {
        input.addEventListener('input', () => syncCount(input.closest('[data-strategy-fields]')));
    }

    initSettings(form);
    syncStrategy();
}
