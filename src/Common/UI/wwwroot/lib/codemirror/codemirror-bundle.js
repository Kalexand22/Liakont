/**
 * codemirror-bundle.js — Stratum Code Editor bundle (self-contained, no CDN)
 *
 * Implements window.StratumCodeMirror with the API surface consumed by
 * code-editor.js.module. Uses a textarea/pre-overlay approach:
 *   - Edit mode: transparent textarea on top, syntax-highlighted <pre> behind
 *   - ReadOnly mode: <pre><code> rendered directly
 *
 * Syntax tokenizers (regex-based, no external deps):
 *   plaintext, csharp, json, xml, html, sql, javascript, markdown
 *
 * Replaces: this bundle is self-contained; no further build step is needed.
 * Upgrade path: swap for a real CodeMirror 6 esbuild bundle to gain LSP,
 *   code folding, vim mode, etc.  The StratumCodeMirror API surface is stable.
 */
(function (global) {
    'use strict';

    // ── Tokenizer registry ──────────────────────────────────────────────────

    /**
     * Escapes HTML special chars to safely inject into innerHTML.
     * @param {string} text
     * @returns {string}
     */
    function _esc(text) {
        return text
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    }

    /**
     * Wraps a run of text in a <span> with the given token class.
     * @param {string} cls  e.g. "cm-keyword"
     * @param {string} text
     * @returns {string}
     */
    function _span(cls, text) {
        return '<span class="' + cls + '">' + _esc(text) + '</span>';
    }

    // ── Per-language tokenizers ─────────────────────────────────────────────

    /**
     * Generic regex-based tokenizer.
     * Rules: array of [RegExp, className]. Applied in order.
     * Unmatched text is escaped and emitted verbatim.
     * @param {string} code
     * @param {Array<[RegExp, string]>} rules
     * @returns {string} HTML
     */
    function _tokenize(code, rules) {
        if (!code) return '';

        let html = '';
        let pos = 0;

        while (pos < code.length) {
            let matched = false;

            for (const [rx, cls] of rules) {
                rx.lastIndex = pos;
                const m = rx.exec(code);
                if (m && m.index === pos) {
                    html += cls ? _span(cls, m[0]) : _esc(m[0]);
                    pos += m[0].length;
                    matched = true;
                    break;
                }
            }

            if (!matched) {
                // Emit one character verbatim to avoid infinite loops.
                html += _esc(code[pos]);
                pos++;
            }
        }

        return html;
    }

    // C# tokenizer
    const _CS_KW = /\b(abstract|as|base|bool|break|byte|case|catch|char|checked|class|const|continue|decimal|default|delegate|do|double|else|enum|event|explicit|extern|false|finally|fixed|float|for|foreach|goto|if|implicit|in|int|interface|internal|is|lock|long|namespace|new|null|object|operator|out|override|params|private|protected|public|readonly|record|ref|return|sbyte|sealed|short|sizeof|stackalloc|static|string|struct|switch|this|throw|true|try|typeof|uint|ulong|unchecked|unsafe|ushort|using|virtual|void|volatile|while|var|async|await|get|set|init|value|global|partial|required|file|scoped|nint|nuint|and|or|not|with|when)\b/gy;
    const _CS_RULES = [
        [/\/\/[^\n]*/gy,                          'cm-comment'],
        [/\/\*[\s\S]*?\*\//gy,                    'cm-comment'],
        [/"(?:[^"\\]|\\.)*"/gy,                   'cm-string'],
        [/\$"(?:[^"\\]|\\.|\{[^}]*\})*"/gy,       'cm-string'],
        [/@"(?:[^"]|"")*"/gy,                     'cm-string'],
        [/'(?:[^'\\]|\\.){1}'/gy,                 'cm-string'],
        [_CS_KW,                                   'cm-keyword'],
        [/\b\d+(?:\.\d+)?(?:[fFdDmM]|[uU][lL]?|[lL])?\b/gy, 'cm-number'],
        [/[{}[\]().,;:+\-*/%=<>!&|^~?]/gy,        null],
    ];

    // JSON tokenizer
    const _JSON_RULES = [
        [/"(?:[^"\\]|\\.)*"\s*(?=:)/gy,           'cm-json-key'],
        [/"(?:[^"\\]|\\.)*"/gy,                   'cm-string'],
        [/\b(true|false|null)\b/gy,               'cm-keyword'],
        [/-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b/gy, 'cm-number'],
        [/[{}[\],]/gy,                             null],
        [/:/gy,                                    null],
    ];

    // JavaScript tokenizer
    const _JS_KW = /\b(break|case|catch|class|const|continue|debugger|default|delete|do|else|export|extends|false|finally|for|function|if|import|in|instanceof|let|new|null|of|return|static|super|switch|this|throw|true|try|typeof|undefined|var|void|while|with|yield|async|await|from|get|set)\b/gy;
    const _JS_RULES = [
        [/\/\/[^\n]*/gy,                          'cm-comment'],
        [/\/\*[\s\S]*?\*\//gy,                    'cm-comment'],
        [/`(?:[^`\\]|\\.|\$\{[^}]*\})*`/gy,      'cm-string'],
        [/"(?:[^"\\]|\\.)*"/gy,                   'cm-string'],
        [/'(?:[^'\\]|\\.)*'/gy,                   'cm-string'],
        [_JS_KW,                                   'cm-keyword'],
        [/\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b/gy, 'cm-number'],
        [/[{}[\]().,;:+\-*/%=<>!&|^~?]/gy,        null],
    ];

    // SQL tokenizer
    const _SQL_KW = /\b(SELECT|FROM|WHERE|JOIN|LEFT|RIGHT|INNER|OUTER|FULL|ON|AS|AND|OR|NOT|IN|EXISTS|LIKE|IS|NULL|ORDER|BY|GROUP|HAVING|LIMIT|OFFSET|UNION|ALL|DISTINCT|INSERT|INTO|VALUES|UPDATE|SET|DELETE|CREATE|TABLE|DROP|ALTER|INDEX|VIEW|PRIMARY|KEY|FOREIGN|REFERENCES|CASCADE|DEFAULT|CONSTRAINT|UNIQUE|AUTO_INCREMENT|SERIAL|INTEGER|INT|BIGINT|SMALLINT|VARCHAR|CHAR|TEXT|BOOLEAN|BOOL|DATE|TIMESTAMP|FLOAT|DOUBLE|DECIMAL|NUMERIC|COUNT|SUM|AVG|MIN|MAX|CASE|WHEN|THEN|ELSE|END|WITH|CROSS|NATURAL)\b/giy;
    const _SQL_RULES = [
        [/--[^\n]*/gy,                            'cm-comment'],
        [/\/\*[\s\S]*?\*\//gy,                    'cm-comment'],
        [/'(?:[^'\\]|''|\\.)*'/gy,               'cm-string'],
        [_SQL_KW,                                  'cm-keyword'],
        [/\b\d+(?:\.\d+)?\b/gy,                  'cm-number'],
        [/[(),;]/gy,                               null],
    ];

    // XML/HTML shared base
    function _tokenizeXmlHtml(code) {
        if (!code) return '';

        // States: normal text, tag content, attribute value
        const TAG_RULE = /(<\/?)([A-Za-z][A-Za-z0-9._-]*)((?:\s[^>]*)?)(\/?>)|<!--[\s\S]*?-->|&[#A-Za-z0-9]+;/gy;
        let html = '';
        let pos = 0;

        TAG_RULE.lastIndex = 0;
        let m;
        while ((m = TAG_RULE.exec(code)) !== null) {
            // Emit text before match
            if (m.index > pos) {
                html += _esc(code.slice(pos, m.index));
            }

            if (m[0].startsWith('<!--')) {
                html += _span('cm-comment', m[0]);
            } else if (m[0].startsWith('&')) {
                html += _span('cm-string', m[0]);
            } else {
                // Tag: bracket + name + attrs + closing bracket
                const openBracket = m[1]; // '<' or '</'
                const tagName = m[2];
                const attrs = m[3] || '';
                const closeBracket = m[4]; // '>' or '/>'

                html += _esc(openBracket);
                html += _span('cm-tag', tagName);
                // Highlight attribute names and values
                html += attrs.replace(
                    /(\s+)([A-Za-z_:][A-Za-z0-9_:.-]*)(\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]*))?/g,
                    (full, space, attrName, attrVal) => {
                        let out = _esc(space) + _span('cm-attr', attrName);
                        if (attrVal) {
                            const eqIdx = attrVal.indexOf('=');
                            const eq = attrVal.slice(0, eqIdx + 1);
                            const val = attrVal.slice(eqIdx + 1);
                            out += _esc(eq) + _span('cm-string', val);
                        }
                        return out;
                    }
                );
                html += _esc(closeBracket);
            }

            pos = m.index + m[0].length;
        }

        // Remaining text
        if (pos < code.length) {
            html += _esc(code.slice(pos));
        }

        return html;
    }

    // Markdown tokenizer
    function _tokenizeMarkdown(code) {
        if (!code) return '';

        // Inline rules for _tokenize: tokens that are safe to highlight.
        // All unmatched text is passed through _esc() by _tokenize,
        // preventing XSS from raw HTML embedded in markdown content.
        const MD_INLINE = [
            [/`[^`]+`/gy,                                                  'cm-md-code'],
            [/\*\*[^*\n]+\*\*/gy,                                          'cm-md-bold'],
            [/__[^_\n]+__/gy,                                              'cm-md-bold'],
            [/\*[^*\n]+\*/gy,                                              'cm-md-italic'],
            [/_[^_\n]+_/gy,                                                'cm-md-italic'],
            [/~~[^~\n]+~~/gy,                                              'cm-comment'],
            [/\[[^\]\n]*\]\([^)\n]*\)/gy,                                  'cm-string'],
        ];

        return code
            .split('\n')
            .map(line => {
                // Headings
                if (/^#{1,6}\s/.test(line)) {
                    return _span('cm-md-heading', line);
                }
                // Horizontal rule
                if (/^---+$|^===+$/.test(line.trim())) {
                    return _span('cm-comment', line);
                }
                // Blockquote
                if (/^>/.test(line)) {
                    return _span('cm-comment', line);
                }
                // Inline tokenization via _tokenize — unmatched chars are _esc()d,
                // preventing injection of raw HTML tags present in markdown text.
                return _tokenize(line, MD_INLINE);
            })
            .join('\n');
    }

    /**
     * Dispatches to the correct tokenizer.
     * @param {string} code
     * @param {string} lang
     * @returns {string} HTML
     */
    function _highlight(code, lang) {
        switch (lang) {
            case 'csharp':     return _tokenize(code, _CS_RULES);
            case 'json':       return _tokenize(code, _JSON_RULES);
            case 'javascript': return _tokenize(code, _JS_RULES);
            case 'sql':        return _tokenize(code, _SQL_RULES);
            case 'xml':        return _tokenizeXmlHtml(code);
            case 'html':       return _tokenizeXmlHtml(code);
            case 'markdown':   return _tokenizeMarkdown(code);
            default:           return _esc(code);
        }
    }

    // ── Editor instances ────────────────────────────────────────────────────

    /** @type {Map<Element, EditorInstance>} */
    const _instances = new Map();

    /**
     * @typedef {{
     *   container: Element,
     *   pre: HTMLPreElement,
     *   textarea: HTMLTextAreaElement|null,
     *   onChange: function|null,
     *   language: string,
     *   readOnly: boolean,
     *   lineNumbers: boolean,
     *   gutter: HTMLElement|null,
     * }} EditorInstance
     */

    /**
     * Builds and returns line-number HTML for the given code.
     * @param {string} code
     * @returns {string}
     */
    function _buildGutterHtml(code) {
        const count = (code.match(/\n/g) || []).length + 1;
        let html = '';
        for (let i = 1; i <= count; i++) {
            html += '<div class="cm-gutter-element">' + i + '</div>';
        }
        return html;
    }

    /**
     * Creates an editor on the given container.
     * @param {Element} container
     * @param {{ value: string, language: string, readOnly: boolean, lineNumbers: boolean, theme: string, onChange: function }} options
     * @returns {{ _container: Element }}
     */
    function createEditor(container, options) {
        const lang = options.language || 'plaintext';
        const readOnly = options.readOnly ?? false;
        const lineNumbers = options.lineNumbers !== false;
        const value = options.value || '';

        container.classList.add('cm-editor-container');
        if (options.theme === 'dark') {
            container.classList.add('cm-theme-dark');
        }

        // Lang badge
        if (lang && lang !== 'plaintext') {
            const LABELS = {
                csharp: 'C#', json: 'JSON', xml: 'XML', sql: 'SQL',
                javascript: 'JavaScript', html: 'HTML', markdown: 'Markdown',
            };
            const badge = document.createElement('span');
            badge.className = 'cm-lang-badge';
            badge.textContent = LABELS[lang] || lang;
            badge.setAttribute('aria-hidden', 'true');
            container.appendChild(badge);
        }

        // Scroller row
        const scroller = document.createElement('div');
        scroller.className = 'cm-scroller';
        container.appendChild(scroller);

        // Gutter
        let gutter = null;
        if (lineNumbers) {
            gutter = document.createElement('div');
            gutter.className = 'cm-gutters';
            gutter.setAttribute('aria-hidden', 'true');
            gutter.innerHTML = _buildGutterHtml(value);
            scroller.appendChild(gutter);
        }

        // Editor surface (holds overlay pre + textarea)
        const surface = document.createElement('div');
        surface.className = 'cm-surface';
        scroller.appendChild(surface);

        // Highlighted pre (always present)
        const pre = document.createElement('pre');
        pre.className = 'cm-highlight';
        pre.setAttribute('aria-hidden', 'true');
        const codeEl = document.createElement('code');
        codeEl.innerHTML = _highlight(value, lang) || '\n'; // trailing \n prevents last-line collapse
        pre.appendChild(codeEl);
        surface.appendChild(pre);

        let textarea = null;

        if (!readOnly) {
            textarea = document.createElement('textarea');
            textarea.className = 'cm-input';
            textarea.value = value;
            textarea.spellcheck = false;
            textarea.autocomplete = 'off';
            textarea.autocapitalize = 'off';
            textarea.setAttribute('role', 'textbox');
            textarea.setAttribute('aria-multiline', 'true');
            surface.appendChild(textarea);

            textarea.addEventListener('input', () => {
                const v = textarea.value;
                // Read inst.language at event time so setLanguage() changes take effect.
                codeEl.innerHTML = _highlight(v, inst.language) || '\n';
                if (gutter) gutter.innerHTML = _buildGutterHtml(v);
                if (options.onChange) options.onChange(v);
            });

            // Sync scroll between textarea and pre
            textarea.addEventListener('scroll', () => {
                pre.scrollTop = textarea.scrollTop;
                pre.scrollLeft = textarea.scrollLeft;
            });

        } else {
            pre.setAttribute('tabindex', '0');
            pre.setAttribute('role', 'textbox');
            pre.setAttribute('aria-multiline', 'true');
            pre.setAttribute('aria-readonly', 'true');
        }

        /** @type {EditorInstance} */
        const inst = {
            container, pre, codeEl, textarea, onChange: options.onChange || null,
            language: lang, readOnly, lineNumbers, gutter,
        };
        _instances.set(container, inst);

        return { _container: container };
    }

    /**
     * Sets the editor value without triggering onChange.
     * @param {{ _container: Element }} view
     * @param {string} value
     */
    function setValue(view, value) {
        const inst = _instances.get(view._container);
        if (!inst) return;

        if (inst.textarea) inst.textarea.value = value;
        inst.codeEl.innerHTML = _highlight(value, inst.language) || '\n';
        if (inst.gutter) inst.gutter.innerHTML = _buildGutterHtml(value);
    }

    /**
     * Replaces the active language / syntax.
     * @param {{ _container: Element }} view
     * @param {string} languageKey
     */
    function setLanguage(view, languageKey) {
        const inst = _instances.get(view._container);
        if (!inst) return;

        inst.language = languageKey;

        // Update badge
        const LABELS = {
            csharp: 'C#', json: 'JSON', xml: 'XML', sql: 'SQL',
            javascript: 'JavaScript', html: 'HTML', markdown: 'Markdown', plaintext: '',
        };
        const badge = inst.container.querySelector('.cm-lang-badge');
        const label = LABELS[languageKey] ?? languageKey;
        if (label) {
            if (badge) {
                badge.textContent = label;
            } else {
                const b = document.createElement('span');
                b.className = 'cm-lang-badge';
                b.textContent = label;
                b.setAttribute('aria-hidden', 'true');
                inst.container.insertBefore(b, inst.container.firstChild);
            }
        } else if (badge) {
            badge.remove();
        }

        // Re-highlight current value
        const current = inst.textarea ? inst.textarea.value : (inst.codeEl.textContent || '');
        inst.codeEl.innerHTML = _highlight(current, languageKey) || '\n';
    }

    /**
     * Toggles read-only mode.
     * @param {{ _container: Element }} view
     * @param {boolean} readOnly
     */
    function setReadOnly(view, readOnly) {
        const inst = _instances.get(view._container);
        if (!inst || !inst.textarea) return;

        inst.textarea.readOnly = readOnly;
        if (readOnly) {
            inst.textarea.setAttribute('aria-readonly', 'true');
        } else {
            inst.textarea.removeAttribute('aria-readonly');
        }
    }

    /**
     * Destroys the editor and clears the container.
     * @param {{ _container: Element }} view
     */
    function destroy(view) {
        const inst = _instances.get(view._container);
        if (!inst) return;

        inst.container.innerHTML = '';
        inst.container.className = inst.container.className
            .replace(/\bcm-[^\s]+/g, '').trim();
        _instances.delete(view._container);
    }

    // ── Export ──────────────────────────────────────────────────────────────
    global.StratumCodeMirror = { createEditor, setValue, setLanguage, setReadOnly, destroy };

})(typeof globalThis !== 'undefined' ? globalThis : window);
