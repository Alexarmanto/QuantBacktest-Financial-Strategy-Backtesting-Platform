// One submission only for the refresh forms of the data pages (P4-U10, as P4-U2): <form data-refresh-form>.
// The listener is on document, like the one of confirm.js, and registered after it: both are module scripts, run in
// document order, and confirm.js comes first in the layout. So when this listener runs, confirm.js has already asked
// its question; if the user cancelled, the event is defaultPrevented and the button stays enabled.
// Invalid forms (required, pattern) never fire submit. Without JavaScript the form is sent directly.

document.addEventListener('submit', (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || !form.hasAttribute('data-refresh-form')) {
        return;
    }

    if (form.dataset.submitting === 'true') {
        event.preventDefault();
        return;
    }

    if (event.defaultPrevented) {
        return;
    }

    setBusy(form, true);
});

// Back button: the page may come from the back/forward cache with the button still disabled.
window.addEventListener('pageshow', () => {
    for (const form of document.querySelectorAll('form[data-refresh-form]')) {
        setBusy(form, false);
    }
});

function setBusy(form, busy) {
    form.dataset.submitting = busy ? 'true' : 'false';
    const submit = form.querySelector('[data-submit]');
    const spinner = form.querySelector('[data-spinner]');
    if (submit) {
        submit.disabled = busy;
    }

    if (spinner) {
        spinner.hidden = !busy;
    }
}
