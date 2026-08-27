export function initNumericGuard() {
    // Block non-numeric characters from being typed into numeric fields.
    // MudBlazor's numeric field only sanitizes on blur, so without this guard users can
    // type '.', ',', '-', 'e', '+' which are then rejected after the fact.
    const allowedKeys = new Set([
        'Backspace', 'Delete', 'Tab', 'Enter', 'Escape',
        'ArrowLeft', 'ArrowRight', 'Home', 'End'
    ]);

    document.addEventListener('keydown', (e) => {
        const el = e.target;
        if (!(el instanceof HTMLInputElement)) return;
        if (!el.closest('.sdt-numeric')) return;

        // Allow copy/paste/select-all/undo shortcuts.
        if (e.ctrlKey || e.metaKey) return;

        // Allow navigation/control keys.
        if (allowedKeys.has(e.key)) return;

        // Block any single printable character that isn't a digit.
        if (e.key.length === 1 && !/[0-9]/.test(e.key)) {
            e.preventDefault();
        }
    });
}
