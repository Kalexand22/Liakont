// Stratum Common UI — Advanced Grid ES Module
// Handles: Row DnD Reorder (HTML5 drag-and-drop), CSV Export (file download).
// Loaded via dynamic import() from StratumDataGrid when any advanced feature is active.
//
// Ref: orchestration/items/UI_D.yaml — UI_D03

'use strict';

// ── Row DnD Reorder ───────────────────────────────────────────────────────────

/** @type {WeakMap<Element, ReorderState>} wrapperEl → state */
const _reorderState = new WeakMap();

/**
 * Initialises HTML5 drag-and-drop row reorder for the given grid wrapper.
 * Emits NotifyRowReorder(fromIndex, toIndex) on successful drop.
 * @param {Element} wrapperEl - The .stratum-datagrid-wrapper element.
 * @param {import('@microsoft/dotnet-js-interop').DotNetObjectReference} dotnetRef
 */
export function initRowReorder(wrapperEl, dotnetRef) {
    if (!wrapperEl) return;

    // Query tbody dynamically to handle @key-driven grid re-mounts
    const getTbody = () => wrapperEl.querySelector('tbody');

    if (!getTbody()) return;

    let dragFromIndex = -1;
    /** @type {HTMLTableRowElement|null} */
    let dragIndicator = null;

    const removeDragIndicator = () => {
        if (dragIndicator?.parentNode) dragIndicator.parentNode.removeChild(dragIndicator);
        dragIndicator = null;
    };

    const getRowIndex = (row) => {
        const tbody = getTbody();
        if (!tbody) return -1;
        const rows = Array.from(tbody.querySelectorAll('tr.rz-data-row'));
        return rows.indexOf(row);
    };

    const onDragStart = (e) => {
        const handle = e.target.closest('.stratum-datagrid__drag-handle');
        if (!handle) return;
        const row = handle.closest('tr.rz-data-row');
        if (!row) return;

        dragFromIndex = getRowIndex(row);
        if (dragFromIndex < 0) return;

        e.dataTransfer.effectAllowed = 'move';
        e.dataTransfer.setData('text/plain', String(dragFromIndex));
        row.classList.add('stratum-datagrid__row--dragging');
    };

    const onDragOver = (e) => {
        const row = e.target.closest('tr.rz-data-row');
        if (!row) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';

        const tbody = getTbody();
        if (!tbody) return;

        if (!dragIndicator) {
            dragIndicator = document.createElement('tr');
            dragIndicator.className = 'stratum-datagrid__drop-indicator';
            dragIndicator.setAttribute('aria-hidden', 'true');
            const td = document.createElement('td');
            td.colSpan = 100;
            dragIndicator.appendChild(td);
        }

        const rect = row.getBoundingClientRect();
        if (e.clientY < rect.top + rect.height / 2) {
            tbody.insertBefore(dragIndicator, row);
        } else {
            tbody.insertBefore(dragIndicator, row.nextSibling);
        }
    };

    const onDragLeave = (e) => {
        const tbody = getTbody();
        if (!tbody || !tbody.contains(e.relatedTarget)) removeDragIndicator();
    };

    const onDrop = (e) => {
        e.preventDefault();
        const tbody = getTbody();
        if (dragFromIndex < 0 || !dragIndicator?.parentNode || !tbody) {
            removeDragIndicator();
            dragFromIndex = -1;
            return;
        }

        const dataRows = Array.from(tbody.querySelectorAll('tr.rz-data-row'));
        let toIndex = dataRows.length;
        for (let i = 0; i < dataRows.length; i++) {
            if (dragIndicator.compareDocumentPosition(dataRows[i]) & Node.DOCUMENT_POSITION_FOLLOWING) {
                toIndex = i;
                break;
            }
        }

        removeDragIndicator();
        const draggingRow = tbody.querySelector('.stratum-datagrid__row--dragging');
        if (draggingRow) draggingRow.classList.remove('stratum-datagrid__row--dragging');

        const from = dragFromIndex;
        dragFromIndex = -1;

        if (from >= 0 && from < dataRows.length && toIndex !== from && toIndex !== from + 1) {
            const adjustedTo = toIndex > from ? toIndex - 1 : toIndex;
            dotnetRef.invokeMethodAsync('NotifyRowReorder', from, adjustedTo);
        }
    };

    const onDragEnd = () => {
        const tbody = getTbody();
        const draggingRow = tbody?.querySelector('.stratum-datagrid__row--dragging');
        if (draggingRow) draggingRow.classList.remove('stratum-datagrid__row--dragging');
        removeDragIndicator();
        dragFromIndex = -1;
    };

    wrapperEl.addEventListener('dragstart', onDragStart);
    wrapperEl.addEventListener('dragover', onDragOver);
    wrapperEl.addEventListener('dragleave', onDragLeave);
    wrapperEl.addEventListener('drop', onDrop);
    wrapperEl.addEventListener('dragend', onDragEnd);

    _reorderState.set(wrapperEl, {
        dotnetRef, onDragStart, onDragOver, onDragLeave, onDrop, onDragEnd
    });
}

/**
 * Tears down row reorder listeners.
 * @param {Element} wrapperEl
 */
export function destroyRowReorder(wrapperEl) {
    if (!wrapperEl) return;
    const state = _reorderState.get(wrapperEl);
    if (state) {
        wrapperEl.removeEventListener('dragstart', state.onDragStart);
        wrapperEl.removeEventListener('dragover', state.onDragOver);
        wrapperEl.removeEventListener('dragleave', state.onDragLeave);
        wrapperEl.removeEventListener('drop', state.onDrop);
        wrapperEl.removeEventListener('dragend', state.onDragEnd);
    }
    _reorderState.delete(wrapperEl);
}

// ── CSV Export with UTF-8 BOM ─────────────────────────────────────────────────

/**
 * Triggers a client-side CSV file download with UTF-8 BOM for Excel compatibility.
 * @param {string} fileName - File name including extension.
 * @param {string} csvContent - CSV content (without BOM; BOM is added automatically).
 */
export function downloadCsvWithBom(fileName, csvContent) {
    const BOM = '\ufeff';
    const blob = new Blob([BOM + csvContent], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}
