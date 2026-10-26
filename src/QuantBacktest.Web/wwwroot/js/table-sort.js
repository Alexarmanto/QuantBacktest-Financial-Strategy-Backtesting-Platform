// Client-side sorting of table[data-sortable] (P4-U6). Each header holds a <button data-sort-type="number|text">,
// so it is reachable and usable with the keyboard. Cells are sorted on their data-sort-value (raw invariant value:
// ISO date, number with "." as decimal point), never on the formatted text. First click: ascending; next: reversed.
// The initial order is the order of the server; ties keep their relative order.

for (const table of document.querySelectorAll('table[data-sortable]')) {
    initTable(table);
}

function initTable(table) {
    const body = table.tBodies[0];
    const headers = [...table.tHead.rows[0].cells];
    [...body.rows].forEach((row, index) => { row.dataset.originalIndex = String(index); });

    headers.forEach((header, column) => {
        const button = header.querySelector('button[data-sort-type]');
        if (!button) {
            return;
        }

        button.addEventListener('click', () => {
            const ascending = header.getAttribute('aria-sort') !== 'ascending';
            for (const other of headers) {
                other.removeAttribute('aria-sort');
            }

            header.setAttribute('aria-sort', ascending ? 'ascending' : 'descending');
            sortRows(body, column, button.dataset.sortType, ascending);
        });
    });
}

function sortRows(body, column, type, ascending) {
    const direction = ascending ? 1 : -1;
    const key = (row) => row.cells[column].dataset.sortValue ?? '';
    const rows = [...body.rows].sort((a, b) => {
        const order = compare(key(a), key(b), type) * direction;
        return order !== 0 ? order : Number(a.dataset.originalIndex) - Number(b.dataset.originalIndex);
    });

    body.append(...rows);
}

function compare(a, b, type) {
    if (type === 'number') {
        return Number(a) - Number(b);
    }

    // Ordinal comparison: ISO dates sort correctly as text.
    return a < b ? -1 : a > b ? 1 : 0;
}
