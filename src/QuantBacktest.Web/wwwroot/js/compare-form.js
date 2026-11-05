// Comparator form (P4-X1). The server renders a working form (boxes already disabled for the timeframe of the URL)
// and stays the authority on every rule: this module only refines it. Range, messages and supported timeframes come
// from data-* attributes. Shared settings behaviour (dates, ]min, max] ranges, one submission): backtest-form.js.
import { initSettings } from './backtest-form.js';

const form = document.getElementById('compare-form');
if (form) {
    init(form);
}

function init(form) {
    const timeframe = form.elements.namedItem('tf');
    const range = form.querySelector('[data-strategy-range]');
    const counter = form.querySelector('[data-strategy-count]');
    const boxes = [...form.querySelectorAll('input[type="checkbox"][name="strategies"]')];
    const min = Number(range.dataset.min);
    const max = Number(range.dataset.max);

    // A strategy that does not support the timeframe is disabled and unchecked: a disabled box is never sent, so it
    // must not look selected. Its label already says why ("daily only").
    function syncTimeframe() {
        for (const box of boxes) {
            const supported = box.dataset.timeframes.split(' ').includes(timeframe.value);
            if (!supported) {
                box.checked = false;
            }

            box.disabled = !supported;
        }

        syncCount();
    }

    // Counter read by screen readers (aria-live), and the browser blocks the submission outside [min, max].
    function syncCount() {
        const count = boxes.filter(box => box.checked).length;
        counter.textContent = `${count} selected (${min} to ${max})`;
        const message = count < min ? range.dataset.tooFew : count > max ? range.dataset.tooMany : '';
        for (const box of boxes) {
            box.setCustomValidity('');
        }

        const target = boxes.find(box => !box.disabled);
        target?.setCustomValidity(message);
    }

    timeframe.addEventListener('change', syncTimeframe);
    for (const box of boxes) {
        box.addEventListener('change', syncCount);
    }

    initSettings(form);
    syncTimeframe();
}
