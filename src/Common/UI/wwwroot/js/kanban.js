/**
 * kanban.js — UF02
 * ES module for Kanban<TItem, TColumn> keyboard Space-key handling and drag-drop.
 */

const _state = new Map();

export function initKeyboard(containerId) {
    const container = document.getElementById(containerId);
    if (!container) return;
    const onKeydown = (e) => { if (e.key === ' ' && e.target.closest('[data-kanban-item-index]')) e.preventDefault(); };
    const existing = _state.get(containerId);
    if (existing) { if (existing.handlers.onKeydown) container.removeEventListener('keydown', existing.handlers.onKeydown, true); existing.handlers.onKeydown = onKeydown; }
    else { _state.set(containerId, { dotnetRef: null, handlers: { onKeydown } }); }
    container.addEventListener('keydown', onKeydown, true);
}

export function destroyKeyboard(containerId) {
    const entry = _state.get(containerId);
    if (!entry || !entry.handlers.onKeydown) return;
    const container = document.getElementById(containerId);
    if (container) container.removeEventListener('keydown', entry.handlers.onKeydown, true);
    _state.delete(containerId);
}

export function initKanban(containerId, dotnetRef) {
    const container = document.getElementById(containerId);
    if (!container) return;

    const onDragStart = (e) => {
        const card = e.target.closest('[data-kanban-item-index]');
        if (!card) return;
        e.dataTransfer.effectAllowed = 'move';
        e.dataTransfer.setData('text/plain', JSON.stringify({ itemIndex: card.dataset.kanbanItemIndex, colIdx: card.dataset.kanbanColIdx }));
        card.classList.add('str-kanban__card--dragging');
    };
    const onDragEnd = (e) => {
        const card = e.target.closest('[data-kanban-item-index]');
        if (card) card.classList.remove('str-kanban__card--dragging');
        container.querySelectorAll('.str-kanban__col-body--drag-over').forEach(el => el.classList.remove('str-kanban__col-body--drag-over'));
    };
    const onDragOver = (e) => {
        const dz = e.target.closest('[data-kanban-dropzone]');
        if (!dz) return;
        e.preventDefault(); e.dataTransfer.dropEffect = 'move';
        container.querySelectorAll('.str-kanban__col-body--drag-over').forEach(el => { if (el !== dz) el.classList.remove('str-kanban__col-body--drag-over'); });
        dz.classList.add('str-kanban__col-body--drag-over');
    };
    const onDragLeave = (e) => {
        if (!container.contains(e.relatedTarget)) container.querySelectorAll('.str-kanban__col-body--drag-over').forEach(el => el.classList.remove('str-kanban__col-body--drag-over'));
    };
    const onDrop = async (e) => {
        const dz = e.target.closest('[data-kanban-dropzone]');
        if (!dz) return;
        e.preventDefault(); dz.classList.remove('str-kanban__col-body--drag-over');
        let payload; try { payload = JSON.parse(e.dataTransfer.getData('text/plain')); } catch { return; }
        const toLocalIdx = computeDropIndex(dz, e.clientY);
        try { await dotnetRef.invokeMethodAsync('OnCardDropped', String(payload.itemIndex), String(dz.dataset.kanbanDropzone), String(toLocalIdx)); }
        catch (err) { const m = err?.message || ''; if (!m.includes('circuit') && !m.includes('disconnected') && !m.includes('disposed')) throw err; }
    };

    container.addEventListener('dragstart', onDragStart);
    container.addEventListener('dragend', onDragEnd);
    container.addEventListener('dragover', onDragOver);
    container.addEventListener('dragleave', onDragLeave);
    container.addEventListener('drop', onDrop);

    const existing = _state.get(containerId);
    if (existing) { existing.dotnetRef = dotnetRef; Object.assign(existing.handlers, { onDragStart, onDragEnd, onDragOver, onDragLeave, onDrop }); }
    else { _state.set(containerId, { dotnetRef, handlers: { onDragStart, onDragEnd, onDragOver, onDragLeave, onDrop } }); }
}

function computeDropIndex(dropZone, clientY) {
    const cards = Array.from(dropZone.querySelectorAll('[data-kanban-item-index]:not(.str-kanban__card--dragging)'));
    for (let i = 0; i < cards.length; i++) { const r = cards[i].getBoundingClientRect(); if (clientY < r.top + r.height / 2) return i; }
    return cards.length;
}

export function destroyKanban(containerId) {
    const entry = _state.get(containerId);
    if (!entry) return;
    const container = document.getElementById(containerId);
    if (container) {
        const h = entry.handlers;
        if (h.onDragStart) container.removeEventListener('dragstart', h.onDragStart);
        if (h.onDragEnd) container.removeEventListener('dragend', h.onDragEnd);
        if (h.onDragOver) container.removeEventListener('dragover', h.onDragOver);
        if (h.onDragLeave) container.removeEventListener('dragleave', h.onDragLeave);
        if (h.onDrop) container.removeEventListener('drop', h.onDrop);
    }
    delete entry.handlers.onDragStart; delete entry.handlers.onDragEnd;
    delete entry.handlers.onDragOver; delete entry.handlers.onDragLeave;
    delete entry.handlers.onDrop; entry.dotnetRef = null;
}
