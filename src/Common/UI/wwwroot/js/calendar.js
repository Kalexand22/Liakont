/**
 * calendar.js — UF01
 * ES module for Calendar<TItem> keyboard handling and drag-drop.
 */

/** @type {Map<string, { dotnetRef: any|null, handlers: object }>} */
const _state = new Map();
const PREVENT_DEFAULT_KEYS = new Set(['ArrowLeft', 'ArrowRight']);

export function initKeyboard(containerId) {
    const container = document.getElementById(containerId);
    if (!container) return;
    const entry = _state.get(containerId);
    const onKeydown = (e) => {
        if (e.target === container && PREVENT_DEFAULT_KEYS.has(e.key)) e.preventDefault();
    };
    if (entry) { entry.handlers.onKeydown = onKeydown; }
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

export function initCalendar(containerId, dotnetRef) {
    const container = document.getElementById(containerId);
    if (!container) return;

    const onDragStart = (e) => {
        const chip = e.target.closest('[data-cal-event-index]');
        if (!chip) return;
        e.dataTransfer.effectAllowed = 'move';
        e.dataTransfer.setData('text/plain', chip.dataset.calEventIndex);
    };
    const onDragOver = (e) => {
        if (!e.target.closest('[data-cal-slot]')) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';
    };
    const onDrop = async (e) => {
        const cell = e.target.closest('[data-cal-slot]');
        if (!cell) return;
        e.preventDefault();
        const itemIndex = e.dataTransfer.getData('text/plain');
        try { await dotnetRef.invokeMethodAsync('OnEventMoved', itemIndex, cell.dataset.calSlot, cell.dataset.calSlotEnd || ''); }
        catch (err) { const m = err?.message || ''; if (!m.includes('circuit') && !m.includes('disconnected') && !m.includes('disposed')) throw err; }
    };
    const onDragEnd = () => {};

    container.addEventListener('dragstart', onDragStart);
    container.addEventListener('dragover', onDragOver);
    container.addEventListener('drop', onDrop);
    container.addEventListener('dragend', onDragEnd);

    const existing = _state.get(containerId);
    if (existing) { existing.dotnetRef = dotnetRef; Object.assign(existing.handlers, { onDragStart, onDragOver, onDrop, onDragEnd }); }
    else { _state.set(containerId, { dotnetRef, handlers: { onDragStart, onDragOver, onDrop, onDragEnd } }); }
}

export function destroyCalendar(containerId) {
    const entry = _state.get(containerId);
    if (!entry) return;
    const container = document.getElementById(containerId);
    if (container) {
        const h = entry.handlers;
        container.removeEventListener('dragstart', h.onDragStart);
        container.removeEventListener('dragover', h.onDragOver);
        container.removeEventListener('drop', h.onDrop);
        container.removeEventListener('dragend', h.onDragEnd);
    }
    delete entry.handlers.onDragStart; delete entry.handlers.onDragOver;
    delete entry.handlers.onDrop; delete entry.handlers.onDragEnd;
    entry.dotnetRef = null;
}
