// Small browser hooks the Blazor app calls through JS interop.
window.soapAndSoul = {
    /** Calls dotnetRef.FlushAsync() when the page is hidden (app switched, screen off, tab closed). */
    onHidden(dotnetRef) {
        const handler = () => {
            if (document.visibilityState === 'hidden') dotnetRef.invokeMethodAsync('FlushAsync');
        };
        document.addEventListener('visibilitychange', handler);
        window.addEventListener('pagehide', handler);
        return {
            dispose() {
                document.removeEventListener('visibilitychange', handler);
                window.removeEventListener('pagehide', handler);
            },
        };
    },

    /** Scrolls a swipe row back to its resting position. */
    resetSwipe(el) {
        el?.scrollTo({ left: 0, behavior: 'smooth' });
    },

    focus(el) {
        el?.focus();
        el?.select?.();
    },
};
