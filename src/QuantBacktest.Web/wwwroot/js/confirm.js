// Generic confirmation before a form is sent: <form data-confirm="Question?">. The browser asks the question and
// cancels the submission unless the user confirms. Without JavaScript the form is sent directly; the server checks
// everything anyway (antiforgery token, id).

document.addEventListener('submit', (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || !form.hasAttribute('data-confirm')) {
        return;
    }

    if (!window.confirm(form.dataset.confirm)) {
        event.preventDefault();
    }
});
