// Rule builder (P4-U9): adds and removes condition rows, shows the operand fields of the chosen type. No rule of the
// contract here: the server renders which field belongs to which type (data-kinds), the bounds (data-min, data-max),
// the counter text and the template of a new row, and ICustomStrategyFactory.Validate checks everything on save.
//
// A new row takes the largest index of the rows of its rule + 1, read from their field names, never the number of
// rows (after a removal that number can be an index still in use). The server reads the rows in the order of their
// index, gaps included, so a removal never renumbers the field names; only the visible "Condition N" is renumbered.

const INDEX_PLACEHOLDER = /__index__/g;
const ROW_INDEX = /\.Conditions\[(\d+)\]\./;

const nextIndexOf = (list) => {
    let largest = -1;
    for (const field of list.querySelectorAll(':scope > [data-condition] [name]')) {
        const match = ROW_INDEX.exec(field.name);
        if (match) {
            largest = Math.max(largest, Number(match[1]));
        }
    }

    return largest + 1;
};

for (const element of document.querySelectorAll('[data-js-only]')) {
    element.hidden = false;
}

for (const rule of document.querySelectorAll('[data-rule]')) {
    const list = rule.querySelector('[data-condition-list]');
    const template = rule.querySelector('template[data-condition-template]');
    const addButton = rule.querySelector('[data-add-condition]');
    const count = rule.querySelector('[data-condition-count]');
    const min = Number(rule.dataset.min);
    const max = Number(rule.dataset.max);

    const refresh = () => {
        const rows = list.querySelectorAll(':scope > [data-condition]');
        rows.forEach((row, position) => {
            row.querySelector('[data-condition-number]').textContent = String(position + 1);
            row.querySelector('[data-remove-condition]').disabled = rows.length <= min;
        });
        addButton.disabled = rows.length >= max;
        count.textContent = String(rows.length);
    };

    addButton.addEventListener('click', () => {
        const index = nextIndexOf(list);
        list.insertAdjacentHTML('beforeend', template.innerHTML.replace(INDEX_PLACEHOLDER, String(index)));
        const row = list.lastElementChild;
        for (const element of row.querySelectorAll('[data-js-only]')) {
            element.hidden = false;
        }

        refresh();
        row.querySelector('select')?.focus();
    });

    list.addEventListener('click', (event) => {
        const button = event.target.closest('[data-remove-condition]');
        if (!button) {
            return;
        }

        button.closest('[data-condition]').remove();
        refresh();
        addButton.focus();
    });

    refresh();
}

document.addEventListener('change', (event) => {
    const select = event.target.closest('[data-operand-kind]');
    if (!select) {
        return;
    }

    for (const field of select.closest('[data-operand]').querySelectorAll('[data-kinds]')) {
        field.hidden = !field.dataset.kinds.split(' ').includes(select.value);
    }
});
