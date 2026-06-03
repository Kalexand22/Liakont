// MultiSelectDropdown.razor.js — keyboard preventDefault interop for MultiSelectDropdown.
// Prevents default scroll behavior for Arrow / Space keys without the Blazor
// render-time off-by-one that @onkeydown:preventDefault="@field" causes.

/**
 * Attaches a keydown listener to the given root element that calls
 * preventDefault for navigation keys (ArrowDown, ArrowUp, Space).
 * @param {HTMLElement} el  The dropdown root element (@ref target)
 * @returns {{ dispose: () => void }}  Disposable subscription
 */
export function attach(el) {
    const h = (e) => {
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === ' ' || e.key === 'Spacebar') {
            e.preventDefault();
        }
    };
    el.addEventListener('keydown', h);
    return { dispose: () => el.removeEventListener('keydown', h) };
}
