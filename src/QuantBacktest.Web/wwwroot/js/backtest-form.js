// Backtest form (P4-U1, P4-U2) and "Copy link" button of the result page.
// The server renders a working form: this module only refines it. All data comes from data-* attributes.

const form = document.getElementById('backtest-form');
if (form) {
    initForm(form);
}

for (const button of document.querySelectorAll('[data-copy-link]')) {
    button.addEventListener('click', () => copyLink(button));
}

function initForm(form) {
    const strategy = form.elements.namedItem('strategy');
    const timeframe = form.elements.namedItem('tf');

    // Only the selected strategy's parameters are enabled: a disabled fieldset submits nothing.
    function syncStrategy() {
        for (const fieldset of form.querySelectorAll('[data-strategy-fields]')) {
            const selected = fieldset.dataset.strategyFields === strategy.value;
            fieldset.disabled = !selected;
            fieldset.hidden = !selected;
        }

        // The option stays enabled (a disabled selected option would not be sent); the select becomes invalid.
        const option = strategy.selectedOptions[0];
        const supported = !option || option.dataset.timeframes.split(' ').includes(timeframe.value);
        strategy.setCustomValidity(supported ? '' : `This strategy is ${option.dataset.restriction}.`);
    }

    strategy.addEventListener('change', syncStrategy);
    timeframe.addEventListener('change', syncStrategy);
    initSettings(form);
    syncStrategy();
}

// The settings every backtest form shares (partials _MarketFields and _ExecutionFields): period order, ]min, max]
// ranges, and one submission only (P4-U2). Exported for the comparator form (compare-form.js).
export function initSettings(form) {
    const from = form.elements.namedItem('from');
    const to = form.elements.namedItem('to');
    const submit = form.querySelector('[data-submit]');
    const spinner = form.querySelector('[data-spinner]');
    let submitting = false;

    function syncDates() {
        const reversed = from.value && to.value && from.value > to.value;
        from.setCustomValidity(reversed ? 'The start date must be on or before the end date.' : '');
    }

    // ]min, max] ranges (capital, stop-loss, take-profit, trailing stop): HTML min is inclusive. The server only
    // writes data-exclusive-min on those fields (NumberSetting); an empty bound is never a check.
    function syncExclusiveMin(input) {
        const bound = input.dataset.exclusiveMin;
        const tooLow = bound !== '' && input.value !== '' && Number(input.value) <= Number(bound);
        input.setCustomValidity(tooLow ? `Must be greater than ${bound}.` : '');
    }

    function setBusy(busy) {
        submitting = busy;
        submit.disabled = busy;
        spinner.hidden = !busy;
    }

    from.addEventListener('change', syncDates);
    to.addEventListener('change', syncDates);
    const exclusive = form.querySelectorAll('[data-exclusive-min]');
    for (const input of exclusive) {
        input.addEventListener('input', () => syncExclusiveMin(input));
        syncExclusiveMin(input);
    }

    syncDates();

    // P4-U2: one submission only, with a visible loading state. Invalid forms never reach this handler.
    form.addEventListener('submit', (event) => {
        if (submitting) {
            event.preventDefault();
            return;
        }

        setBusy(true);
    });

    // Back button: the page may come from the back/forward cache with the button still disabled.
    window.addEventListener('pageshow', () => setBusy(false));
}

// navigator.clipboard only exists in a secure context (HTTPS or localhost). Elsewhere (Docker reached by IP),
// or if the copy is refused, the link is shown in a read-only input, selected, ready for Ctrl+C.
async function copyLink(button) {
    const label = button.textContent;
    try {
        if (!navigator.clipboard) {
            throw new Error('Clipboard unavailable');
        }

        await navigator.clipboard.writeText(button.dataset.copyLink);
        button.textContent = 'Copied!';
    } catch {
        if (showFallback(button)) {
            return;
        }

        button.textContent = 'Copy failed: use the address bar';
    }

    setTimeout(() => { button.textContent = label; }, 2000);
}

function showFallback(button) {
    const container = document.getElementById(button.getAttribute('aria-controls'));
    const input = container?.querySelector('input');
    if (!input) {
        return false;
    }

    container.hidden = false;
    input.focus();
    input.select();
    return true;
}
