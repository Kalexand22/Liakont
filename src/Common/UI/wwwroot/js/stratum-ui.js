// Stratum Common UI — client-side helpers
// Referenced by components that require direct DOM interaction.
// Import: <script src="_content/Stratum.Common.UI/js/stratum-ui.js"></script>

// Load Radzen JS (required by RadzenDataGrid, RadzenDropDown, RadzenDatePicker).
// Injected here so the Host never references Radzen directly.
(function () {
    const src = '/_content/Radzen.Blazor/Radzen.Blazor.js';
    if (!document.querySelector('script[src="' + src + '"]')) {
        const s = document.createElement('script');
        s.src = src;
        s.async = false;          // preserve execution order (before blazor.web.js)
        const anchor = document.currentScript;
        if (anchor) {
            anchor.parentNode.insertBefore(s, anchor.nextSibling);
        } else {
            document.head.appendChild(s);
        }
    }
})();

// ── Theme: apply saved preference (or OS default) before first paint ──
// Primary application happens in an inline <script> in App.razor <head> (before CSS).
// This IIFE serves as a fallback for the initial load and watches for
// Blazor enhanced navigation stripping the data-theme attribute.
(function () {
    var apply = function () {
        var saved = localStorage.getItem('stratum-theme');
        if (!saved) saved = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
        document.documentElement.setAttribute('data-theme', saved);
    };
    apply();

    // Blazor enhanced navigation patches <html> attributes from the server response,
    // which does not include data-theme — removing it. A MutationObserver detects this
    // and immediately re-applies the saved preference. This runs before the browser
    // recalculates styles, so there is no visible flash.
    var _applying = false;
    new MutationObserver(function (mutations) {
        if (_applying) return;
        for (var i = 0; i < mutations.length; i++) {
            if (mutations[i].attributeName === 'data-theme') {
                var current = document.documentElement.getAttribute('data-theme');
                var saved = localStorage.getItem('stratum-theme');
                if (!saved) saved = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
                if (current !== saved) {
                    _applying = true;
                    document.documentElement.setAttribute('data-theme', saved);
                    _applying = false;
                }
                break;
            }
        }
    }).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
})();

// ── Density (UIF06): apply saved preference before first paint ──
// Same shape as the theme IIFE above. Primary application is the inline
// <script> in App.razor <head>; this module-level IIFE handles late imports
// (SSR + enhanced navigation) and the MutationObserver watches for Blazor
// stripping data-density on navigation.
(function () {
    var valid = function (v) { return v === 'compact' || v === 'standard'; };
    var apply = function () {
        var saved = localStorage.getItem('stratum-density');
        if (!valid(saved)) saved = 'compact';
        document.documentElement.setAttribute('data-density', saved);
    };
    apply();

    var _applyingDensity = false;
    new MutationObserver(function (mutations) {
        if (_applyingDensity) return;
        for (var i = 0; i < mutations.length; i++) {
            if (mutations[i].attributeName === 'data-density') {
                var current = document.documentElement.getAttribute('data-density');
                var saved = localStorage.getItem('stratum-density');
                if (!valid(saved)) saved = 'compact';
                if (current !== saved) {
                    _applyingDensity = true;
                    document.documentElement.setAttribute('data-density', saved);
                    _applyingDensity = false;
                }
                break;
            }
        }
    }).observe(document.documentElement, { attributes: true, attributeFilter: ['data-density'] });
})();

window.stratumUI = {
    /**
     * Focuses the data row at the given index within a SimpleTable.
     * @param {string} tableId - The id attribute of the <table> element.
     * @param {number} index   - Zero-based row index in <tbody>.
     */
    focusRow(tableId, index) {
        const table = document.getElementById(tableId);
        if (!table) return;
        const rows = table.querySelectorAll('tbody tr[data-row-index]');
        const target = Array.from(rows).find(r => r.dataset.rowIndex === String(index));
        if (target) {
            target.setAttribute('tabindex', '0');
            target.focus({ preventScroll: false });
        }
    },

    /**
     * Registers a FilterBar search input to receive focus when "/" is pressed
     * outside of any text input field.
     * @param {string} inputId - The id attribute of the search <input> element.
     */
    registerFilterBar(inputId) {
        this._filterBarHandlers = this._filterBarHandlers || new Map();
        const handler = (e) => {
            if (
                e.key === '/' &&
                !e.ctrlKey && !e.altKey && !e.metaKey &&
                !['INPUT', 'TEXTAREA', 'SELECT'].includes(e.target.tagName)
            ) {
                const el = document.getElementById(inputId);
                if (el) {
                    e.preventDefault();
                    el.focus();
                    el.select();
                }
            }
        };
        this._filterBarHandlers.set(inputId, handler);
        document.addEventListener('keydown', handler);
    },

    /**
     * Unregisters the FilterBar "/" shortcut handler for the given input.
     * @param {string} inputId - The id attribute of the search <input> element.
     */
    unregisterFilterBar(inputId) {
        const handler = this._filterBarHandlers?.get(inputId);
        if (handler) {
            document.removeEventListener('keydown', handler);
            this._filterBarHandlers.delete(inputId);
        }
    },

    /**
     * Scrolls a Lookup listbox option into view (nearest).
     * @param {string} listboxId - The id attribute of the <ul role="listbox"> element.
     * @param {string} optionId  - The id attribute of the <li role="option"> to scroll into view.
     */
    scrollLookupOptionIntoView(listboxId, optionId) {
        const option = document.getElementById(optionId);
        if (option) option.scrollIntoView({ block: 'nearest' });
    },

    /**
     * Focuses a DOM element by its id attribute.
     * @param {string} elementId - The id of the element to focus.
     */
    focusElement(elementId) {
        const el = document.getElementById(elementId);
        if (el) el.focus();
    },

    /**
     * Saves the currently focused element onto a stack so it can be restored after a modal closes.
     * Supports nested dialogs: each saveFocus pushes, each restoreFocus pops.
     * Call this immediately before opening a modal or dialog.
     */
    saveFocus() {
        this._focusStack = this._focusStack || [];
        this._focusStack.push(document.activeElement);
    },

    /**
     * Restores focus to the most recently saved element (stack pop).
     * Call this after a modal or dialog closes (ARIA best practice).
     * Safe to call even if the stack is empty.
     */
    restoreFocus() {
        this._focusStack = this._focusStack || [];
        const el = this._focusStack.pop();
        if (el && el.isConnected && typeof el.focus === 'function') {
            el.focus();
        }
    },

    /**
     * Prevents the browser default action when Enter is pressed inside an input element.
     * Used by MoneyField to stop accidental form submission when the user presses Enter
     * to confirm the amount. Uses capture to run before form submit handlers.
     * @param {string} inputId - The id attribute of the <input> element.
     */
    preventEnterDefault(inputId) {
        this._enterHandlers = this._enterHandlers || new Map();
        if (this._enterHandlers.has(inputId)) return; // already registered
        const handler = (e) => {
            if (e.key === 'Enter') e.preventDefault();
        };
        this._enterHandlers.set(inputId, handler);
        const el = document.getElementById(inputId);
        if (el) el.addEventListener('keydown', handler, { capture: true });
    },

    /**
     * Removes the Enter-prevention handler for a MoneyField input.
     * @param {string} inputId - The id attribute of the <input> element.
     */
    removeEnterDefault(inputId) {
        const handler = this._enterHandlers?.get(inputId);
        if (!handler) return;
        const el = document.getElementById(inputId);
        if (el) el.removeEventListener('keydown', handler, { capture: true });
        this._enterHandlers.delete(inputId);
    },

    /**
     * Registers a selective keydown preventDefault handler on a StratumDataGrid wrapper.
     * Only prevents default for keys the grid handles (arrows, Space, Home, End, Enter, x).
     * @param {Element} wrapperEl - The wrapper element reference.
     */
    registerDataGridKeyNav(wrapperEl) {
        if (!wrapperEl) return;
        this._dataGridHandlers = this._dataGridHandlers || new WeakMap();
        if (this._dataGridHandlers.has(wrapperEl)) return;
        const GRID_KEYS = new Set([
            'ArrowUp', 'ArrowDown', 'Home', 'End', ' ', 'x'
        ]);
        const handler = (e) => {
            if (GRID_KEYS.has(e.key)) e.preventDefault();
        };
        this._dataGridHandlers.set(wrapperEl, handler);
        wrapperEl.addEventListener('keydown', handler, { capture: false });
    },

    /**
     * Unregisters the StratumDataGrid keydown handler.
     * @param {Element} wrapperEl - The wrapper element reference.
     */
    unregisterDataGridKeyNav(wrapperEl) {
        if (!wrapperEl || !this._dataGridHandlers) return;
        const handler = this._dataGridHandlers.get(wrapperEl);
        if (handler) {
            wrapperEl.removeEventListener('keydown', handler, { capture: false });
            this._dataGridHandlers.delete(wrapperEl);
        }
    },

    /**
     * Focuses a row in a StratumDataGrid by index.
     * Finds the nth data row within the Radzen grid wrapper.
     * @param {Element} wrapperEl - The wrapper element reference.
     * @param {number} index      - Zero-based row index.
     */
    /**
     * Installs a keydown listener on the grid wrapper that calls preventDefault
     * only for navigation keys consumed by HandleKeyDown, and only when the
     * event target is the wrapper itself (not a descendant input/textarea).
     * This replaces the static @onkeydown:preventDefault="true" which blocked
     * typing in inline-edit cells and column filter inputs.
     * @param {Element} wrapperEl - The wrapper element reference.
     */
    installGridKeyGuard(wrapperEl) {
        if (!wrapperEl || wrapperEl.__stratumKeyGuard) return;
        const navKeys = new Set([
            'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight',
            'Home', 'End', ' ', 'Escape'
        ]);
        wrapperEl.addEventListener('keydown', function (e) {
            // Only prevent default when focus is on the wrapper itself,
            // not on descendant inputs (inline-edit, filter, etc.)
            var tag = e.target.tagName;
            if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT'
                || e.target.isContentEditable) {
                return;
            }
            if (navKeys.has(e.key) || (e.ctrlKey && (e.key === 'a' || e.key === 'A'))) {
                e.preventDefault();
            }
        });
        wrapperEl.__stratumKeyGuard = true;
    },

    focusDataGridRow(wrapperEl, index) {
        if (!wrapperEl) return;
        const i = parseInt(index, 10);
        if (!Number.isFinite(i)) return;
        const rows = wrapperEl.querySelectorAll('tbody > tr[data-row-index]');
        // Clear previous tabindex from all rows (roving tabindex pattern)
        rows.forEach(r => r.setAttribute('tabindex', '-1'));
        const target = wrapperEl.querySelector(`tbody > tr[data-row-index="${i}"]`);
        if (target) {
            target.setAttribute('tabindex', '0');
            target.focus({ preventScroll: false });
        }
    },

    /**
     * Updates row--active and row--selected CSS classes on grid rows.
     * Called after keyboard navigation or selection changes to ensure
     * visual feedback is in sync, bypassing Radzen's render cycle.
     * Uses data-row-index attribute (set via OnRowRender) for stable
     * targeting that won't break with Radzen structural rows.
     * @param {Element} wrapperEl      - The wrapper element reference.
     * @param {number} activeIndex     - Index of the focused row (-1 for none).
     * @param {number[]} selectedIndices - Array of selected row indices.
     */
    updateGridRowClasses(wrapperEl, activeIndex, selectedIndices) {
        if (!wrapperEl) return;
        const rows = wrapperEl.querySelectorAll('tbody > tr[data-row-index]');
        const selectedSet = new Set(selectedIndices);
        rows.forEach(r => {
            const idx = parseInt(r.getAttribute('data-row-index'), 10);
            r.classList.toggle('row--active', idx === activeIndex);
            r.classList.toggle('row--selected', selectedSet.has(idx));
        });
    },

    // ── Viewport ────────────────────────────────────────────────────────────

    /**
     * Returns the current browser viewport dimensions.
     * Used by GFI09 cell-menu positioning to clamp the menu within the viewport.
     * @returns {{ width: number, height: number }}
     */
    getViewport() {
        return { width: window.innerWidth, height: window.innerHeight };
    },

    // ── Clipboard ───────────────────────────────────────────────────────────

    /**
     * Copies the given text to the system clipboard.
     * Uses navigator.clipboard when available (HTTPS / localhost), falls back
     * to a hidden textarea + document.execCommand('copy') otherwise. Used by
     * GFI09 "Copier" context-menu action on StratumDataGrid cells.
     * @param {string} text - The text to copy.
     * @returns {Promise<boolean>} True on success, false otherwise.
     */
    async copyToClipboard(text) {
        const payload = text ?? '';
        try {
            if (navigator.clipboard && window.isSecureContext) {
                await navigator.clipboard.writeText(payload);
                return true;
            }
        } catch (e) {
            // Fall through to legacy path
        }
        try {
            const ta = document.createElement('textarea');
            ta.value = payload;
            ta.setAttribute('readonly', '');
            ta.style.position = 'absolute';
            ta.style.left = '-9999px';
            document.body.appendChild(ta);
            ta.select();
            const ok = document.execCommand('copy');
            document.body.removeChild(ta);
            return ok;
        } catch (e) {
            return false;
        }
    },

    // ── File download (export) ─────────────────────────────────────────────

    /**
     * Triggers a file download in the browser by creating a temporary Blob URL.
     * Used by StratumDataGrid CSV export.
     * @param {string} fileName - The file name including extension (e.g. "export.csv").
     * @param {string} content  - The file content as a UTF-8 string.
     * @param {string} mimeType - The MIME type (e.g. "text/csv").
     */
    downloadFile(fileName, content, mimeType) {
        const blob = new Blob([content], { type: mimeType + ';charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        a.style.display = 'none';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    },

    /**
     * Triggers a file download from a byte array (Base64-encoded by Blazor).
     * Used by StratumDataGrid PDF export.
     * @param {string} fileName - The file name including extension (e.g. "export.pdf").
     * @param {Uint8Array} bytes - The file content as a byte array (Blazor marshals byte[] as Uint8Array).
     * @param {string} mimeType - The MIME type (e.g. "application/pdf").
     */
    downloadFileBytes(fileName, bytes, mimeType) {
        const blob = new Blob([bytes], { type: mimeType });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        a.style.display = 'none';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    },

    // ── Performance measurement ─────────────────────────────────────────

    /**
     * Measures the time from the 'stratum-grid-before' performance mark
     * to the next animation frame (paint). Returns the duration in ms.
     * Used by the virtual scroll performance demo.
     * @returns {Promise<number>} Paint duration in milliseconds.
     */
    measureGridPaint() {
        return new Promise((resolve) => {
            requestAnimationFrame(() => {
                requestAnimationFrame(() => {
                    let duration = -1;
                    try {
                        performance.mark('stratum-grid-after');
                        performance.measure('stratum-grid-paint', 'stratum-grid-before', 'stratum-grid-after');
                        const entries = performance.getEntriesByName('stratum-grid-paint');
                        duration = entries.length > 0 ? entries[entries.length - 1].duration : -1;
                    } catch {
                        // Mark may be absent — duration stays -1
                    } finally {
                        // Always clean up marks/measures to prevent leaks
                        try { performance.clearMarks('stratum-grid-before'); } catch { /* noop */ }
                        try { performance.clearMarks('stratum-grid-after'); } catch { /* noop */ }
                        try { performance.clearMeasures('stratum-grid-paint'); } catch { /* noop */ }
                    }
                    resolve(duration);
                });
            });
        });
    },

    /** No-op function for SignalR round-trip measurement (CSP-safe). */
    noop() { },

    // ── TextArea auto-resize ──────────────────────────────────────────

    /**
     * Namespace for TextArea auto-resize helpers.
     */
    textArea: {
        /**
         * Initialises auto-resize on a textarea element.
         * Sets the height to scrollHeight on every input event.
         * @param {HTMLTextAreaElement} el - The textarea element reference.
         */
        initAutoResize(el) {
            if (!el) return;
            this._handlers = this._handlers || new WeakMap();
            if (this._handlers.has(el)) return;

            const resize = () => {
                el.style.height = 'auto';
                el.style.height = el.scrollHeight + 'px';
            };
            this._handlers.set(el, resize);
            el.addEventListener('input', resize);
            // Initial resize for pre-filled content
            resize();
        },

        /**
         * Removes the auto-resize handler from a textarea element.
         * @param {HTMLTextAreaElement} el - The textarea element reference.
         */
        disposeAutoResize(el) {
            if (!el || !this._handlers) return;
            const handler = this._handlers.get(el);
            if (handler) {
                el.removeEventListener('input', handler);
                this._handlers.delete(el);
            }
        }
    },

    // ── Theme management ──────────────────────────────────────────────

    /**
     * Returns the current effective theme ('light' or 'dark').
     * Checks the data-theme attribute first, then falls back to OS preference.
     * @returns {string} 'light' or 'dark'
     */
    getTheme() {
        const attr = document.documentElement.getAttribute('data-theme');
        if (attr === 'dark' || attr === 'light') return attr;
        return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    },

    /**
     * Sets the theme and persists the preference in localStorage.
     * @param {string} theme - 'light' or 'dark'
     */
    setTheme(theme) {
        document.documentElement.setAttribute('data-theme', theme);
        localStorage.setItem('stratum-theme', theme);
    },

    /**
     * Toggles between light and dark themes.
     * @returns {string} The new theme after toggling.
     */
    toggleTheme() {
        const next = this.getTheme() === 'dark' ? 'light' : 'dark';
        this.setTheme(next);
        return next;
    },

    // ── Density (UIF06) ────────────────────────────────────────────────

    /**
     * Returns the current density mode ('compact' or 'standard').
     * Checks the data-density attribute first, then falls back to localStorage,
     * then defaults to 'compact' (Civic Blueprint default).
     * @returns {string} 'compact' or 'standard'
     */
    getDensity() {
        const attr = document.documentElement.getAttribute('data-density');
        if (attr === 'compact' || attr === 'standard') return attr;
        const saved = localStorage.getItem('stratum-density');
        return (saved === 'compact' || saved === 'standard') ? saved : 'compact';
    },

    /**
     * Sets the density mode and persists the preference in localStorage.
     * Rejects any value other than 'compact' or 'standard' to keep the
     * attribute contract simple and type-safe on the C# side.
     * @param {string} density - 'compact' or 'standard'
     */
    setDensity(density) {
        if (density !== 'compact' && density !== 'standard') return;
        document.documentElement.setAttribute('data-density', density);
        localStorage.setItem('stratum-density', density);
    },

    /**
     * Toggles between compact and standard density modes.
     * @returns {string} The new density after toggling.
     */
    toggleDensity() {
        const next = this.getDensity() === 'compact' ? 'standard' : 'compact';
        this.setDensity(next);
        return next;
    },

    // ── Grid default page size ─────────────────────────────────────────────

    /**
     * Gets the user's preferred default grid page size from localStorage.
     * @returns {number} The saved page size, or 25 if not set.
     */
    getGridPageSize() {
        const saved = localStorage.getItem('stratum-grid-page-size');
        const parsed = saved ? parseInt(saved, 10) : NaN;
        return [10, 25, 50, 100].includes(parsed) ? parsed : 25;
    },

    /**
     * Sets the user's preferred default grid page size in localStorage.
     * @param {number} size - One of 10, 25, 50, 100.
     */
    setGridPageSize(size) {
        if ([10, 25, 50, 100].includes(size)) {
            localStorage.setItem('stratum-grid-page-size', size.toString());
        }
    },

    // ── Tab session persistence ────────────────────────────────────────

    /**
     * Reads the saved tab state from sessionStorage.
     * @returns {string|null} JSON string of tab state, or null if not set.
     */
    getSessionTabs() {
        try { return sessionStorage.getItem('stratum_tabs'); }
        catch { return null; }
    },

    /**
     * Saves the tab state to sessionStorage.
     * @param {string} json - JSON string of tab state to persist.
     */
    setSessionTabs(json) {
        try { sessionStorage.setItem('stratum_tabs', json); }
        catch { /* quota exceeded or unavailable — silent */ }
    },

    // ── Culture / language switching ─────────────────────────────────────

    /**
     * Sets the .AspNetCore.Culture cookie and forces a full page reload.
     * Used by the language switcher to guarantee a server-side culture change
     * (Blazor Server circuits require a full reload to pick up the new culture).
     * Cookie format matches CookieRequestCultureProvider.MakeCookieValue().
     * @param {string} culture - Culture name (e.g. "en", "fr").
     */
    setCulture(culture) {
        if (!culture || !/^[a-zA-Z]{2}(-[a-zA-Z]{2})?$/.test(culture)) return;
        var value = 'c=' + culture + '|uic=' + culture;
        var expires = new Date();
        expires.setFullYear(expires.getFullYear() + 1);
        var cookie = '.AspNetCore.Culture=' + encodeURIComponent(value)
            + ';expires=' + expires.toUTCString()
            + ';path=/;samesite=lax';
        if (location.protocol === 'https:') cookie += ';secure';
        document.cookie = cookie;
        location.reload();
    },

    // ── Dialog focus trap ──────────────────────────────────────────────────

    /**
     * Registers a focus trap on a dialog element.
     * Tab and Shift+Tab cycle through focusable elements within the dialog.
     * @param {string} dialogId - The id of the dialog container element.
     */
    registerDialogFocusTrap(dialogId) {
        this._dialogTrapHandlers = this._dialogTrapHandlers || new Map();
        if (this._dialogTrapHandlers.has(dialogId)) return;

        const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

        const handler = (e) => {
            if (e.key !== 'Tab') return;
            const dialog = document.getElementById(dialogId);
            if (!dialog) return;

            const focusable = Array.from(dialog.querySelectorAll(FOCUSABLE));
            if (focusable.length === 0) return;

            const first = focusable[0];
            const last = focusable[focusable.length - 1];

            if (e.shiftKey) {
                if (document.activeElement === first || !dialog.contains(document.activeElement)) {
                    e.preventDefault();
                    last.focus();
                }
            } else {
                if (document.activeElement === last || !dialog.contains(document.activeElement)) {
                    e.preventDefault();
                    first.focus();
                }
            }
        };

        this._dialogTrapHandlers.set(dialogId, handler);
        document.addEventListener('keydown', handler, { capture: true });
    },

    /**
     * Removes the focus trap for a dialog.
     * @param {string} dialogId - The id of the dialog container element.
     */
    unregisterDialogFocusTrap(dialogId) {
        const handler = this._dialogTrapHandlers?.get(dialogId);
        if (!handler) return;
        document.removeEventListener('keydown', handler, { capture: true });
        this._dialogTrapHandlers.delete(dialogId);
    },

    // ── Keyboard shortcut system ─────────────────────────────────────────

    /**
     * Shortcut system namespace.
     * Maintains a flat map of active key → commandId bindings and a .NET callback reference.
     * The key map is updated synchronously whenever the scope stack changes (via updateBindings).
     * On keydown, the JS layer decides synchronously whether to consume the event, then invokes
     * the .NET handler asynchronously.
     */
    shortcuts: {
        /**
         * DotNetObjectReference to GlobalShortcutHandler (set by init, cleared by dispose).
         * @type {any}
         */
        _dotNetRef: null,

        /**
         * Flat map of canonical key ID → commandId.
         * Key ID format: [ctrl+][alt+][shift+]key_lower  e.g. "ctrl+s", "?", "escape".
         * @type {Record<string, string>}
         */
        _bindings: {},

        /**
         * Registered keydown handler reference (kept for removeEventListener).
         * @type {((e: KeyboardEvent) => void) | null}
         */
        _handler: null,

        /**
         * Initialises the global keydown listener.
         * @param {any} dotNetRef - DotNetObjectReference<GlobalShortcutHandler>.
         * @param {Record<string, string>} bindings - Initial key map.
         */
        init(dotNetRef, bindings) {
            this._dotNetRef = dotNetRef;
            this._bindings = bindings || {};
            this._handler = (e) => this._onKeyDown(e);
            document.addEventListener('keydown', this._handler, { capture: false });
        },

        /**
         * Replaces the active key map. Called by GlobalShortcutHandler after every scope change.
         * @param {Record<string, string>} bindings - New key map.
         */
        updateBindings(bindings) {
            this._bindings = bindings || {};
        },

        /**
         * Removes the global keydown listener and releases the .NET reference.
         */
        dispose() {
            if (this._handler) {
                document.removeEventListener('keydown', this._handler, { capture: false });
                this._handler = null;
            }
            this._dotNetRef = null;
            this._bindings = {};
        },

        /**
         * Programmatically triggers a shortcut command as if the user pressed the key.
         * Used by static SSR buttons (e.g. ErpTopBar search button) that cannot use @onclick.
         * @param {string} key - The key to simulate (e.g. "k", "?").
         * @param {boolean} [ctrl=false]
         * @param {boolean} [alt=false]
         * @param {boolean} [shift=false]
         */
        simulateKey(key, ctrl, alt, shift) {
            if (!this._dotNetRef) return;
            const keyId = this._keyId(key, !!ctrl, !!alt, !!shift);
            const commandId = this._bindings[keyId];
            if (!commandId) return;
            this._dotNetRef.invokeMethodAsync('ExecuteCommand', commandId)
                .catch(() => { /* Non-critical */ });
        },

        /**
         * Computes the canonical key ID for a KeyboardEvent.
         * Matches the C# ScopeBinding.ComputeKeyId logic.
         *
         * For single non-letter printable characters (e.g. "?", "!", "#"), shiftKey is
         * already implied by the key value itself — no "shift+" prefix is added, so the
         * key ID matches ScopeBinding.ComputeKeyId(key, ctrl, alt, shift: false).
         * For letters, shift IS included so "a" and "A" (shift+a) are distinct.
         *
         * @param {string} key
         * @param {boolean} ctrl
         * @param {boolean} alt
         * @param {boolean} shift
         * @returns {string}
         */
        _keyId(key, ctrl, alt, shift) {
            let id = '';
            if (ctrl)  id += 'ctrl+';
            if (alt)   id += 'alt+';
            // Single non-letter printable chars (e.g. "?") already encode their shift state
            // in the key value itself; including "shift+" would cause a mismatch with C# bindings
            // that register these keys without shift:true.
            const isNonLetterPrintable = key.length === 1 && !/[a-zA-Z]/.test(key);
            if (shift && !isNonLetterPrintable) id += 'shift+';
            id += key.toLowerCase();
            return id;
        },

        /**
         * Global keydown handler.
         * Guard: text inputs only pass Ctrl+/Alt+/Meta+ combos, not bare keys.
         * Guard: elements inside [data-shortcut-boundary] are skipped entirely.
         * If the key matches an active binding, preventDefault + stopPropagation are
         * called synchronously, then the .NET handler is invoked asynchronously.
         * @param {KeyboardEvent} e
         */
        _onKeyDown(e) {
            // Skip pure modifier keys
            if (['Control', 'Alt', 'Shift', 'Meta', 'CapsLock'].includes(e.key)) return;

            const target = e.target;
            const tag = target?.tagName;

            // Guard: elements inside a ShortcutBoundary
            if (target?.closest('[data-shortcut-boundary]')) return;

            const isTextInput = ['INPUT', 'TEXTAREA', 'SELECT'].includes(tag) ||
                                target?.isContentEditable;

            // Guard: in text inputs, only pass explicit modifier combos
            if (isTextInput && !e.ctrlKey && !e.altKey && !e.metaKey) return;

            if (!this._dotNetRef) return;

            const keyId = this._keyId(e.key, e.ctrlKey, e.altKey, e.shiftKey);
            const commandId = this._bindings[keyId];
            if (!commandId) return;

            // Consume the event synchronously before calling .NET
            e.preventDefault();
            e.stopPropagation();

            this._dotNetRef.invokeMethodAsync('ExecuteCommand', commandId)
                .catch(() => { /* Non-critical: .NET handler errors are logged server-side */ });
        }
    },

    // ── Sidebar navigation (keyboard focus) ────────────────────────────

    nav: {
        /**
         * Focuses a navigation tree item by its data-nav-key attribute.
         * Used by ErpNav keyboard navigation (roving tabindex pattern).
         * @param {Element} navElement - The <nav> element reference.
         * @param {string} key - The data-nav-key value of the item to focus.
         */
        focusItem(navElement, key) {
            if (!navElement) return;
            var el = navElement.querySelector('[data-nav-key="' + CSS.escape(key) + '"]');
            if (el) el.focus();
        },

        /**
         * Attaches a keydown listener that calls preventDefault only for
         * WAI-ARIA tree navigation keys (arrows, Home, End).
         * Preserves Enter, Space, Tab and all other keys.
         */
        preventTreeNavKeys(navElement) {
            if (!navElement || navElement._navPreventAttached) return;
            var navKeys = new Set(['ArrowDown', 'ArrowUp', 'ArrowLeft', 'ArrowRight', 'Home', 'End']);
            navElement.addEventListener('keydown', function (e) {
                if (navKeys.has(e.key)) e.preventDefault();
            });
            navElement._navPreventAttached = true;
        },

        /**
         * Returns the saved sidebar collapsed preference from localStorage.
         * @returns {boolean} True if sidebar is collapsed.
         */
        getSidebarCollapsed() {
            return localStorage.getItem('stratum-sidebar-collapsed') === 'true';
        },

        /**
         * Persists the sidebar collapsed state and toggles the CSS class on .erp-shell.
         * @param {boolean} collapsed - Whether the sidebar should be collapsed.
         */
        setSidebarCollapsed(collapsed) {
            localStorage.setItem('stratum-sidebar-collapsed', collapsed ? 'true' : 'false');
            var shell = document.querySelector('.erp-shell');
            if (shell) {
                shell.classList.toggle('sidebar-collapsed', collapsed);
                // On mobile, also close the drawer when collapsing
                if (collapsed && window.innerWidth < 768) {
                    shell.classList.remove('sidebar-open');
                }
            }
            // Remove pre-collapse class (FOUC prevention) once Blazor manages the state
            document.documentElement.classList.remove('sidebar-pre-collapsed');
        },

    },

    // ── Overlay management (z-index, scroll lock, push) ───────────────

    /**
     * Manages z-index stacking, scroll locks, and push-mode offsets
     * for Dialog and Drawer components.
     */
    overlay: {
        /** @type {number} Current z-index counter */
        _zCounter: 0,
        /** @type {number} Base z-index for overlays */
        Z_BASE: 1000,
        /** @type {number} Step between overlay layers */
        Z_STEP: 10,
        /** @type {number} Max stacked overlays */
        Z_MAX: 50,

        /** @type {number} Reference count for scroll locks */
        _scrollLockCount: 0,
        /** @type {string|null} Saved body padding before scroll lock */
        _savedPaddingRight: null,

        /**
         * Acquires a z-index slot for a new overlay.
         * @returns {number} The z-index to use for the overlay element.
         */
        acquireZIndex() {
            if (this._zCounter >= this.Z_MAX) {
                throw new Error('Too many stacked overlays (max ' + this.Z_MAX + ')');
            }
            this._zCounter++;
            return this.Z_BASE + this._zCounter * this.Z_STEP;
        },

        /**
         * Releases a z-index slot.
         */
        releaseZIndex() {
            if (this._zCounter > 0) this._zCounter--;
        },

        /**
         * Acquires a scroll lock on the body.
         * Reference-counted: safe for multiple concurrent overlays.
         */
        acquireScrollLock() {
            this._scrollLockCount++;
            if (this._scrollLockCount === 1) {
                // Save current padding to avoid layout shift from scrollbar removal
                const scrollbarWidth = window.innerWidth - document.documentElement.clientWidth;
                this._savedPaddingRight = document.body.style.paddingRight;
                document.body.style.paddingRight = scrollbarWidth + 'px';
                document.body.style.overflow = 'hidden';
            }
        },

        /**
         * Releases a scroll lock. Restores body overflow when count reaches 0.
         */
        releaseScrollLock() {
            if (this._scrollLockCount > 0) this._scrollLockCount--;
            if (this._scrollLockCount === 0) {
                document.body.style.overflow = '';
                document.body.style.paddingRight = this._savedPaddingRight || '';
                this._savedPaddingRight = null;
            }
        },

        /**
         * Applies a margin to a container element (Drawer push mode).
         * @param {string} selector - CSS selector for the target element.
         * @param {string} property - CSS property name (e.g. "margin-right").
         * @param {string} value - CSS value (e.g. "400px").
         */
        applyPush(selector, property, value) {
            const el = document.querySelector(selector);
            if (!el) return;
            el.style.transition = property + ' 300ms ease';
            el.style[property] = value;
        },

        /**
         * Removes the push margin from a container element.
         * @param {string} selector - CSS selector for the target element.
         * @param {string} property - CSS property name to remove.
         */
        removePush(selector, property) {
            const el = document.querySelector(selector);
            if (!el) return;
            el.style[property] = '';
            // Remove transition after animation completes
            setTimeout(() => {
                if (el) el.style.transition = '';
            }, 300);
        }
    },

    /**
     * Compute how many chip children overflow beyond the container's visible area.
     * Used by FilterChipBar to implement DF-09 (2-line max + "+n autres").
     *
     * Waits one animation frame so the browser finishes layout before measuring.
     * Uses getBoundingClientRect (viewport-relative) for reliable clipped-box
     * detection regardless of offsetParent chain.
     *
     * @param {HTMLElement} container - The chips container element.
     * @returns {Promise<number>} Number of hidden (overflowed) chip children.
     */
    computeChipBarOverflow(container) {
        return new Promise(resolve => {
            if (!container) { resolve(0); return; }
            // Two animation frames — the first commits any pending style updates
            // (including font loading), the second runs after final layout so the
            // container has settled at its clipped height.
            requestAnimationFrame(() => {
                requestAnimationFrame(() => {
                    const cRect = container.getBoundingClientRect();
                    // A child is "overflow-hidden" when its top is at or past the
                    // container's visible bottom edge. 1px slop absorbs sub-pixel
                    // rounding. A child flush with the bottom (top === bottom) is
                    // counted as hidden because zero pixels of it are visible.
                    const bottomLimit = cRect.bottom - 1;
                    let hidden = 0;
                    for (const child of container.children) {
                        const childRect = child.getBoundingClientRect();
                        if (childRect.top >= bottomLimit) {
                            hidden++;
                        }
                    }
                    resolve(hidden);
                });
            });
        });
    }
};
