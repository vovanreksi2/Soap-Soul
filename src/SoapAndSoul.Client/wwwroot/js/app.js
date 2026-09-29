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

    /** Web Speech API, preferring on-device recognition (Chrome's processLocally) when available. */
    speech: {
        supported() {
            return !!(window.SpeechRecognition || window.webkitSpeechRecognition);
        },

        async start(dotnetRef, lang) {
            const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
            const rec = new Recognition();
            rec.lang = lang;
            rec.continuous = true;
            rec.interimResults = true;

            let onDevice = false;
            if ('processLocally' in rec && typeof Recognition.available === 'function') {
                const options = { langs: [lang], processLocally: true };
                try {
                    const status = await Recognition.available(options);
                    if (status === 'available') {
                        rec.processLocally = true;
                        onDevice = true;
                    } else if (status === 'downloadable' && typeof Recognition.install === 'function') {
                        // Fetch the language pack for next time; this session uses the default engine.
                        Recognition.install(options).catch(() => {});
                    }
                } catch { /* fall back to the default engine */ }
            }

            let error = null;
            rec.onresult = e => {
                let final = '', interim = '';
                for (let i = e.resultIndex; i < e.results.length; i++) {
                    const text = e.results[i][0].transcript;
                    if (e.results[i].isFinal) final += text; else interim += text;
                }
                dotnetRef.invokeMethodAsync('OnResult', final, interim);
            };
            rec.onerror = e => { error = e.error; };
            rec.onend = () => dotnetRef.invokeMethodAsync('OnEnd', error);
            rec.start();

            return {
                isOnDevice: () => onDevice,
                stop: () => rec.stop(),
                abort: () => rec.abort(),
            };
        },
    },
};
