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
    const from = form.elements.namedItem('from');
    const to = form.elements.namedItem('to');
    const submit = form.querySelector('[data-submit]');
    const spinner = form.querySelector('[data-spinner]');
    let submitting = false;

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

    function syncDates() {
        const reversed = from.value && to.value && from.value > to.value;
        from.setCustomValidity(reversed ? 'The start date must be on or before the end date.' : '');
    }

    // ]min, max] ranges (capital, stop-loss, take-profit, trailing stop): HTML min is inclusive.
    function syncExclusiveMin(input) {
        const tooLow = input.value !== '' && Number(input.value) <= Number(input.dataset.exclusiveMin);
        input.setCustomValidity(tooLow ? `Must be greater than ${input.dataset.exclusiveMin}.` : '');
    }

    function setBusy(busy) {
        submitting = busy;
        submit.disabled = busy;
        spinner.hidden = !busy;
    }

    strategy.addEventListener('change', syncStrategy);
    timeframe.addEventListener('change', syncStrategy);
    from.addEventListener('change', syncDates);
    to.addEventListener('change', syncDates);
    const exclusive = form.querySelectorAll('[data-exclusive-min]');
    for (const input of exclusive) {
        input.addEventListener('input', () => syncExclusiveMin(input));
        syncExclusiveMin(input);
    }

    syncStrategy();
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

async function copyLink(button) {
    const label = button.textContent;
    try {
        await navigator.clipboard.writeText(button.dataset.copyLink);
        button.textContent = 'Copied!';
    } catch {
        button.textContent = 'Copy failed: use the address bar';
    }

    setTimeout(() => { button.textContent = label; }, 2000);
}
