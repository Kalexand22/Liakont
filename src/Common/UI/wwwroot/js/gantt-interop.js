// gantt-interop.js — drag-resize support for StratumGanttView
// ES module, lazy-loaded by the component.

/** @type {Map<string, AbortController>} */
const _instances = new Map();

/**
 * Initialize drag-resize on a Gantt container.
 * @param {string} instanceId
 * @param {HTMLElement} container
 * @param {object} dotNetRef
 * @param {number} columnWidthPx
 */
export function initDragResize(instanceId, container, dotNetRef, columnWidthPx) {
    dispose(instanceId);

    const ac = new AbortController();
    _instances.set(instanceId, ac);

    let dragging = null;

    container.addEventListener('mousedown', function (e) {
        const handle = e.target.closest('[data-gantt-resize]');
        if (!handle) return;

        const bar = handle.closest('[data-testid^="gantt-bar-"]');
        if (!bar) return;

        e.preventDefault();
        e.stopPropagation();

        const side = handle.dataset.ganttResize;
        const testId = bar.dataset.testid;
        const rowIdx = parseInt(testId.replace('gantt-bar-', ''), 10);

        dragging = {
            bar: bar,
            side: side,
            startX: e.clientX,
            origLeftPx: parseFloat(bar.style.left) || 0,
            origWidthPx: parseFloat(bar.style.width) || 0,
            rowIdx: rowIdx
        };

        document.body.style.cursor = 'ew-resize';
        document.body.style.userSelect = 'none';
        bar.classList.add('stratum-gantt-view__bar--dragging');
    }, { signal: ac.signal });

    document.addEventListener('mousemove', function (e) {
        if (!dragging) return;
        const dx = e.clientX - dragging.startX;

        if (dragging.side === 'end') {
            const newWidth = Math.max(columnWidthPx, dragging.origWidthPx + dx);
            dragging.bar.style.width = newWidth + 'px';
        } else {
            const newLeft = dragging.origLeftPx + dx;
            const newWidth = Math.max(columnWidthPx, dragging.origWidthPx - dx);
            dragging.bar.style.left = newLeft + 'px';
            dragging.bar.style.width = newWidth + 'px';
        }
    }, { signal: ac.signal });

    document.addEventListener('mouseup', function () {
        if (!dragging) return;

        const finalLeft = parseFloat(dragging.bar.style.left) || 0;
        const finalWidth = parseFloat(dragging.bar.style.width) || 0;

        dragging.bar.classList.remove('stratum-gantt-view__bar--dragging');
        document.body.style.cursor = '';
        document.body.style.userSelect = '';

        // Snap to nearest column
        const snappedLeft = Math.round(finalLeft / columnWidthPx) * columnWidthPx;
        const snappedWidth = Math.max(columnWidthPx, Math.round(finalWidth / columnWidthPx) * columnWidthPx);

        dragging.bar.style.left = snappedLeft + 'px';
        dragging.bar.style.width = snappedWidth + 'px';

        const offsetDays = Math.round(snappedLeft / columnWidthPx);
        const durationDays = Math.round(snappedWidth / columnWidthPx);

        dotNetRef.invokeMethodAsync('OnDragResizeComplete', dragging.rowIdx, offsetDays, durationDays);
        dragging = null;
    }, { signal: ac.signal });
}

/**
 * Dispose drag-resize listeners for a Gantt instance.
 * @param {string} instanceId
 */
export function dispose(instanceId) {
    const ac = _instances.get(instanceId);
    if (ac) {
        ac.abort();
        _instances.delete(instanceId);
    }
}
