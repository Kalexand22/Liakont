// NaturalLanguageFilterPanel.razor.js — Web Speech API interop (GFI12)
//
// Wraps the browser SpeechRecognition API so the Blazor component can offer
// voice input next to the text field. The module is intentionally small:
// - `isSupported()` lets the component hide/disable the mic button.
// - `create(...)` returns a handle with `start` / `stop` / `dispose` so the
//   component can own one recognizer per instance without leaking state.
//
// Callbacks to .NET are routed through a DotNetObjectReference with three
// [JSInvokable] endpoints: OnVoiceTranscript(text, isFinal), OnVoiceEnd(),
// OnVoiceError(message). We do the minimum filtering here (final vs interim)
// and let the component decide what to display.

/**
 * @returns {boolean} true when the browser exposes SpeechRecognition
 * (either the standard or the webkit-prefixed variant).
 */
export function isSupported() {
    return typeof window !== 'undefined'
        && (typeof window.SpeechRecognition === 'function'
            || typeof window.webkitSpeechRecognition === 'function');
}

/**
 * Creates a recognizer handle bound to a Blazor component.
 * @param {object} dotnetRef Blazor DotNetObjectReference with JSInvokable
 *     OnVoiceTranscript, OnVoiceEnd, OnVoiceError.
 * @param {string} lang BCP-47 language tag, e.g. "fr-FR".
 * @returns {{ start: () => boolean, stop: () => void, dispose: () => void, isActive: () => boolean }}
 */
export function create(dotnetRef, lang) {
    const Ctor = (typeof window !== 'undefined')
        ? (window.SpeechRecognition || window.webkitSpeechRecognition)
        : null;

    if (!Ctor) {
        return {
            start: () => false,
            stop: () => { },
            dispose: () => { },
            isActive: () => false,
        };
    }

    let recognizer = null;
    let active = false;
    let disposed = false;

    const invokeSafe = (name, ...args) => {
        if (!dotnetRef || disposed) return;
        try {
            dotnetRef.invokeMethodAsync(name, ...args);
        } catch (_) {
            // Component already torn down — nothing we can do.
        }
    };

    const build = () => {
        const r = new Ctor();
        r.lang = lang || 'fr-FR';
        r.interimResults = true;
        r.continuous = false;
        r.maxAlternatives = 1;

        r.onresult = (event) => {
            // Concatenate the final part and the latest interim hypothesis so
            // the component can show a live transcript while the user speaks.
            let finalText = '';
            let interimText = '';
            for (let i = event.resultIndex; i < event.results.length; i++) {
                const result = event.results[i];
                const transcript = result[0]?.transcript ?? '';
                if (result.isFinal) {
                    finalText += transcript;
                } else {
                    interimText += transcript;
                }
            }
            const combined = (finalText + interimText).trim();
            if (combined.length > 0) {
                invokeSafe('OnVoiceTranscript', combined, finalText.length > 0);
            }
        };

        r.onerror = (event) => {
            active = false;
            const message = (event && event.error) ? String(event.error) : 'unknown';
            invokeSafe('OnVoiceError', message);
        };

        r.onend = () => {
            active = false;
            invokeSafe('OnVoiceEnd');
        };

        return r;
    };

    return {
        start: () => {
            if (disposed) return false;
            if (active) return true;
            try {
                recognizer = build();
                recognizer.start();
                active = true;
                return true;
            } catch (_err) {
                active = false;
                return false;
            }
        },
        stop: () => {
            if (!recognizer || !active) return;
            try {
                recognizer.stop();
            } catch (_) {
                // Firefox throws if stop() is called outside an active session;
                // we mirror that by clearing local state and waiting for onend.
            }
        },
        dispose: () => {
            disposed = true;
            if (recognizer) {
                try { recognizer.onresult = null; } catch (_) { }
                try { recognizer.onerror = null; } catch (_) { }
                try { recognizer.onend = null; } catch (_) { }
                try { recognizer.abort(); } catch (_) { }
                recognizer = null;
            }
            active = false;
        },
        isActive: () => active,
    };
}
