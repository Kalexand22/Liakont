// Stratum ERP — BugCapture JS Interop
// Extends window.stratumUI.bugCapture namespace (same pattern as stratum-ui.js).
(function (global) {
    "use strict";

    global.stratumUI = global.stratumUI || {};

    var bugCapture = {
        _html2canvasLoaded: false,
        _mediaRecorder:     null,
        _audioChunks:       [],
        _audioLevelTimer:   null,
        _audioStream:       null,
        _analyser:          null,

        // ── Internal helpers ────────────────────────────────────────────

        _loadHtml2Canvas: function () {
            var self = this;
            if (self._html2canvasLoaded || typeof html2canvas !== "undefined") {
                self._html2canvasLoaded = true;
                return Promise.resolve();
            }
            return new Promise(function (resolve, reject) {
                var script = document.createElement("script");
                script.src = "/_content/Stratum.Common.UI/js/lib/html2canvas.min.js";
                script.onload = function () {
                    self._html2canvasLoaded = true;
                    resolve();
                };
                script.onerror = function () {
                    reject(new Error("Failed to load html2canvas"));
                };
                document.head.appendChild(script);
            });
        },

        _stopTracks: function () {
            if (this._audioStream) {
                this._audioStream.getTracks().forEach(function (t) { t.stop(); });
                this._audioStream = null;
            }
        },

        _clearAudioTimer: function () {
            if (this._audioLevelTimer !== null) {
                clearInterval(this._audioLevelTimer);
                this._audioLevelTimer = null;
            }
        },

        _cleanup: function () {
            this._clearAudioTimer();
            this._stopTracks();
            this._analyser       = null;
            this._mediaRecorder  = null;
            this._audioChunks    = [];
        },

        // ── Screenshot ──────────────────────────────────────────────────

        /**
         * Captures document.body or the element matched by `selector`.
         * @param {string|null} selector  CSS selector or null for full page.
         * @returns {Promise<string>}     Base64-encoded PNG (no data-URI prefix).
         */
        /**
         * Captures the current browser tab using getDisplayMedia.
         * Falls back to html2canvas if getDisplayMedia is unavailable.
         * @returns {Promise<string>} Base64-encoded PNG (no data-URI prefix).
         */
        captureScreenshot: function () {
            var self = this;

            // Prefer native Screen Capture API (real pixel capture).
            if (navigator.mediaDevices && navigator.mediaDevices.getDisplayMedia) {
                return navigator.mediaDevices.getDisplayMedia({
                    video: { displaySurface: "browser" },
                    preferCurrentTab: true
                }).then(function (stream) {
                    var track = stream.getVideoTracks()[0];
                    var capture = new ImageCapture(track);
                    return capture.grabFrame().then(function (bitmap) {
                        track.stop();
                        var canvas = document.createElement("canvas");
                        canvas.width = bitmap.width;
                        canvas.height = bitmap.height;
                        canvas.getContext("2d").drawImage(bitmap, 0, 0);
                        var dataUrl = canvas.toDataURL("image/png");
                        return dataUrl.indexOf(",") !== -1 ? dataUrl.split(",")[1] : dataUrl;
                    });
                }).catch(function (err) {
                    console.warn("[BugCapture] getDisplayMedia failed, falling back to html2canvas:", err);
                    return self._fallbackHtml2Canvas();
                });
            }

            return self._fallbackHtml2Canvas();
        },

        _fallbackHtml2Canvas: function () {
            var self = this;
            return self._loadHtml2Canvas().then(function () {
                return html2canvas(document.body, {
                    useCORS: true,
                    allowTaint: true,
                    logging: false,
                    ignoreElements: function (el) {
                        return el.tagName === "IFRAME";
                    }
                });
            }).then(function (canvas) {
                var dataUrl = canvas.toDataURL("image/png");
                return dataUrl.indexOf(",") !== -1 ? dataUrl.split(",")[1] : dataUrl;
            }).catch(function (err) {
                console.error("[BugCapture] html2canvas failed:", err);
                throw err;
            });
        },

        /**
         * Shows a full-screen crosshair overlay; the user draws a rectangle.
         * @returns {Promise<string>}  Base64-encoded PNG of the selected region.
         */
        captureRegion: function () {
            var self = this;
            return self._loadHtml2Canvas().then(function () {
                return new Promise(function (resolve, reject) {
                    // Overlay
                    var overlay = document.createElement("div");
                    overlay.style.cssText =
                        "position:fixed;top:0;left:0;width:100%;height:100%;" +
                        "z-index:999999;cursor:crosshair;background:rgba(0,0,0,0.15);";

                    // Hint label
                    var hint = document.createElement("div");
                    hint.style.cssText =
                        "position:fixed;top:50%;left:50%;transform:translate(-50%,-50%);" +
                        "color:#fff;background:rgba(0,0,0,0.65);padding:8px 16px;" +
                        "border-radius:4px;font:14px/1.4 sans-serif;pointer-events:none;";
                    hint.textContent = "Draw a rectangle to capture. Press Esc to cancel.";

                    // Selection rectangle
                    var sel = document.createElement("div");
                    sel.style.cssText =
                        "position:fixed;border:2px dashed #3b82f6;" +
                        "background:rgba(59,130,246,0.1);pointer-events:none;display:none;";

                    overlay.appendChild(hint);
                    overlay.appendChild(sel);
                    document.body.appendChild(overlay);

                    var startX = 0;
                    var startY = 0;
                    var dragging = false;

                    function cleanup() {
                        document.body.removeChild(overlay);
                        document.removeEventListener("keydown", onKey);
                    }

                    function onKey(e) {
                        if (e.key === "Escape") {
                            cleanup();
                            reject(new Error("Capture cancelled by user"));
                        }
                    }

                    overlay.addEventListener("mousedown", function (e) {
                        startX   = e.clientX;
                        startY   = e.clientY;
                        dragging = true;
                        hint.style.display = "none";
                        sel.style.display  = "block";
                        sel.style.left     = startX + "px";
                        sel.style.top      = startY + "px";
                        sel.style.width    = "0px";
                        sel.style.height   = "0px";
                    });

                    overlay.addEventListener("mousemove", function (e) {
                        if (!dragging) { return; }
                        var x = Math.min(e.clientX, startX);
                        var y = Math.min(e.clientY, startY);
                        sel.style.left   = x + "px";
                        sel.style.top    = y + "px";
                        sel.style.width  = Math.abs(e.clientX - startX) + "px";
                        sel.style.height = Math.abs(e.clientY - startY) + "px";
                    });

                    overlay.addEventListener("mouseup", function (e) {
                        if (!dragging) { return; }
                        dragging = false;

                        var rx = Math.min(e.clientX, startX);
                        var ry = Math.min(e.clientY, startY);
                        var rw = Math.abs(e.clientX - startX);
                        var rh = Math.abs(e.clientY - startY);

                        cleanup();

                        if (rw < 5 || rh < 5) {
                            reject(new Error("Selection too small"));
                            return;
                        }

                        html2canvas(document.body, {
                            x:            rx + window.scrollX,
                            y:            ry + window.scrollY,
                            width:        rw,
                            height:       rh,
                            windowWidth:  document.documentElement.scrollWidth,
                            windowHeight: document.documentElement.scrollHeight,
                        }).then(function (canvas) {
                            var dataUrl = canvas.toDataURL("image/png");
                            resolve(dataUrl.indexOf(",") !== -1 ? dataUrl.split(",")[1] : dataUrl);
                        }).catch(reject);
                    });

                    document.addEventListener("keydown", onKey);
                });
            });
        },

        // ── Audio recording ─────────────────────────────────────────────

        /**
         * Starts audio recording and streams level updates to .NET.
         * @param {object} dotNetRef  DotNetObjectReference; receives OnAudioLevel(float).
         * @returns {Promise<void>}
         */
        startAudioRecording: function (dotNetRef) {
            var self = this;
            self._audioChunks = [];

            return navigator.mediaDevices.getUserMedia({ audio: true }).then(function (stream) {
                self._audioStream  = stream;
                self._mediaRecorder = new MediaRecorder(stream);

                self._mediaRecorder.ondataavailable = function (e) {
                    if (e.data && e.data.size > 0) {
                        self._audioChunks.push(e.data);
                    }
                };

                // Audio level monitoring via Web Audio API analyser.
                try {
                    var AudioContext = window.AudioContext || window.webkitAudioContext;
                    if (AudioContext) {
                        var ctx    = new AudioContext();
                        var source = ctx.createMediaStreamSource(stream);
                        self._analyser = ctx.createAnalyser();
                        self._analyser.fftSize = 256;
                        source.connect(self._analyser);

                        var buf = new Uint8Array(self._analyser.frequencyBinCount);
                        self._audioLevelTimer = setInterval(function () {
                            if (!self._analyser) { return; }
                            self._analyser.getByteFrequencyData(buf);
                            var sum = 0;
                            for (var i = 0; i < buf.length; i++) { sum += buf[i]; }
                            var level = sum / buf.length / 255;
                            if (dotNetRef) {
                                dotNetRef.invokeMethodAsync("OnAudioLevel", level).catch(function () {});
                            }
                        }, 200);
                    }
                } catch (_) {
                    // Level monitoring is best-effort; recording continues regardless.
                }

                self._mediaRecorder.start(100);
            });
        },

        /**
         * Stops recording and returns the captured audio as a Blob.
         * Blazor wraps the returned Blob as IJSStreamReference on the .NET side.
         * @returns {Promise<Blob>}
         */
        stopAudioRecording: function () {
            var self = this;
            return new Promise(function (resolve) {
                if (!self._mediaRecorder || self._mediaRecorder.state === "inactive") {
                    self._cleanup();
                    resolve(new Blob([], { type: "audio/webm" }));
                    return;
                }

                self._mediaRecorder.onstop = function () {
                    var blob = new Blob(self._audioChunks, { type: "audio/webm" });
                    self._cleanup();
                    resolve(blob);
                };

                self._clearAudioTimer();
                self._mediaRecorder.stop();
                self._stopTracks();
            });
        },

        // ── Screen recording ──────────────────────────────────────────────

        _screenRecorder:  null,
        _screenChunks:    [],
        _screenStream:    null,
        _displayStream:   null,
        _micStream:       null,
        _screenAudioCtx:  null,
        _lastVideoBlob:   null,

        /**
         * Starts recording the current browser tab as video (webm).
         * @returns {Promise<void>}
         */
        startScreenRecording: function () {
            var self = this;
            self._screenChunks = [];

            // Step 1: Get display media first (shows picker UI).
            return navigator.mediaDevices.getDisplayMedia({
                video: { displaySurface: "browser" },
                audio: true,
                preferCurrentTab: true
            }).then(function (displayStream) {
                // Step 2: Request microphone AFTER display picker is dismissed.
                // Try OS default mic first, fall back to Chrome's per-profile preference.
                return navigator.mediaDevices.getUserMedia({ audio: { deviceId: { exact: "default" } } }).catch(function () {
                    return navigator.mediaDevices.getUserMedia({ audio: true });
                }).then(function (micStream) {
                    console.info("[BugCapture] Microphone acquired:", micStream.getAudioTracks()[0].label);
                    return { displayStream: displayStream, micStream: micStream };
                }).catch(function (err) {
                    console.warn("[BugCapture] Microphone unavailable:", err.name, err.message);
                    return { displayStream: displayStream, micStream: null };
                });
            }).then(function (streams) {
                var displayStream = streams.displayStream;
                var micStream = streams.micStream;

                self._displayStream = displayStream;
                self._micStream = micStream;

                var hasMic = micStream && micStream.getAudioTracks().length > 0;
                var hasDisplayAudio = displayStream.getAudioTracks().length > 0;

                // Mic is the priority (user narration). Display audio is secondary.
                // No AudioContext mixing - unreliable across Chrome profiles.
                var tracks = displayStream.getVideoTracks().slice();
                if (hasMic) {
                    tracks.push(micStream.getAudioTracks()[0]);
                } else if (hasDisplayAudio) {
                    displayStream.getAudioTracks().forEach(function (t) { tracks.push(t); });
                } else {
                    console.warn("[BugCapture] No audio sources - video will have no sound");
                }
                return new MediaStream(tracks);
            }).then(function (mixedStream) {
                self._screenStream = mixedStream;
                self._screenRecorder = new MediaRecorder(mixedStream, { mimeType: "video/webm;codecs=vp8,opus" });

                self._screenRecorder.ondataavailable = function (e) {
                    if (e.data && e.data.size > 0) {
                        self._screenChunks.push(e.data);
                    }
                };

                // Auto-stop when user clicks "Stop sharing" in browser chrome.
                var videoTracks = self._displayStream.getVideoTracks();
                if (videoTracks.length > 0) {
                    videoTracks[0].onended = function () {
                        if (self._screenRecorder && self._screenRecorder.state !== "inactive") {
                            self._screenRecorder.stop();
                        }
                    };
                }

                self._screenRecorder.start(100);
            }).catch(function (err) {
                console.error("[BugCapture] Screen recording failed:", err.name, err.message);
                self._stopScreenStreams();
                throw err;
            });
        },

        _stopScreenStreams: function () {
            if (this._displayStream) {
                this._displayStream.getTracks().forEach(function (t) { t.stop(); });
                this._displayStream = null;
            }
            if (this._micStream) {
                this._micStream.getTracks().forEach(function (t) { t.stop(); });
                this._micStream = null;
            }
            if (this._screenStream) {
                this._screenStream = null;
            }
            if (this._screenAudioCtx) {
                this._screenAudioCtx.close().catch(function () {});
                this._screenAudioCtx = null;
            }
        },

        /**
         * Stops screen recording and returns the captured video as a Blob.
         * @returns {Promise<Blob>}
         */
        stopScreenRecording: function () {
            var self = this;
            return new Promise(function (resolve) {
                if (!self._screenRecorder || self._screenRecorder.state === "inactive") {
                    var blob = new Blob(self._screenChunks, { type: "video/webm" });
                    self._lastVideoBlob = blob;
                    self._screenChunks = [];
                    self._stopScreenStreams();
                    self._screenRecorder = null;
                    resolve(blob);
                    return;
                }

                self._screenRecorder.onstop = function () {
                    var blob = new Blob(self._screenChunks, { type: "video/webm" });
                    self._lastVideoBlob = blob;
                    self._screenChunks = [];
                    self._stopScreenStreams();
                    self._screenRecorder = null;
                    resolve(blob);
                };

                self._screenRecorder.stop();
            });
        },

        /**
         * @returns {boolean} True if screen recording is in progress.
         */
        isScreenRecording: function () {
            return this._screenRecorder !== null && this._screenRecorder.state === "recording";
        },

        // ── Frame extraction ─────────────────────────────────────────────

        /**
         * Extracts frames from the last recorded video at given timestamps.
         * Uses the blob kept in memory after stopScreenRecording().
         * @param {number[]} timestamps  Array of seek positions in seconds.
         * @returns {Promise<string[]>}  Array of Base64-encoded PNGs.
         */
        extractFramesFromLastRecording: function (timestamps) {
            var self = this;
            var blob = self._lastVideoBlob;

            if (!blob || blob.size === 0 || !timestamps || timestamps.length === 0) {
                return Promise.resolve([]);
            }

            return new Promise(function (resolve, reject) {
                var video = document.createElement("video");
                video.muted = true;
                video.playsInline = true;

                var url = URL.createObjectURL(blob);
                video.src = url;

                video.onerror = function () {
                    URL.revokeObjectURL(url);
                    reject(new Error("Failed to load video for frame extraction"));
                };

                video.onloadedmetadata = function () {
                    var frames = [];
                    var idx = 0;

                    function seekNext() {
                        if (idx >= timestamps.length) {
                            URL.revokeObjectURL(url);
                            self._lastVideoBlob = null;
                            resolve(frames);
                            return;
                        }
                        var target = Math.min(timestamps[idx], video.duration);
                        video.currentTime = target;
                    }

                    video.onseeked = function () {
                        try {
                            var canvas = document.createElement("canvas");
                            canvas.width = video.videoWidth;
                            canvas.height = video.videoHeight;
                            canvas.getContext("2d").drawImage(video, 0, 0);
                            var dataUrl = canvas.toDataURL("image/png");
                            frames.push(dataUrl.indexOf(",") !== -1 ? dataUrl.split(",")[1] : dataUrl);
                        } catch (_) {
                            frames.push("");
                        }
                        idx++;
                        seekNext();
                    };

                    seekNext();
                };
            });
        },

        /**
         * Clears the retained video blob from memory.
         */
        clearLastVideoBlob: function () {
            this._lastVideoBlob = null;
        },

        // ── Browser console capture ──────────────────────────────────────

        _consoleLogs:       [],
        _originalConsole:   null,
        _consoleCapturing:  false,
        _maxConsoleLogs:    500,

        /**
         * Starts intercepting console.log/warn/error/info/debug.
         * Entries are stored in a circular buffer for later retrieval.
         */
        startConsoleCapture: function () {
            var self = this;
            if (self._consoleCapturing) { return; }

            self._originalConsole = {
                log:   console.log,
                warn:  console.warn,
                error: console.error,
                info:  console.info,
                debug: console.debug,
            };
            self._consoleLogs = [];
            self._consoleCapturing = true;

            var levels = ["log", "warn", "error", "info", "debug"];
            levels.forEach(function (level) {
                var original = self._originalConsole[level];
                console[level] = function () {
                    // Call original first
                    original.apply(console, arguments);
                    try {
                        var parts = [];
                        for (var i = 0; i < arguments.length; i++) {
                            var arg = arguments[i];
                            if (typeof arg === "string") {
                                parts.push(arg);
                            } else {
                                try {
                                    var s = JSON.stringify(arg);
                                    if (s && s.length > 2048) { s = s.substring(0, 2048) + "..."; }
                                    parts.push(s || String(arg));
                                } catch (_) {
                                    parts.push(String(arg));
                                }
                            }
                        }
                        var message = parts.join(" ");
                        self._consoleLogs.push({
                            timestamp: new Date().toISOString(),
                            level: level,
                            message: message,
                        });
                        // Trim buffer
                        if (self._consoleLogs.length > self._maxConsoleLogs) {
                            self._consoleLogs = self._consoleLogs.slice(-self._maxConsoleLogs);
                        }
                    } catch (_) {
                        // Never break the console
                    }
                };
            });
        },

        /**
         * Stops intercepting console methods, restoring originals.
         */
        stopConsoleCapture: function () {
            var self = this;
            if (!self._consoleCapturing || !self._originalConsole) { return; }

            console.log   = self._originalConsole.log;
            console.warn  = self._originalConsole.warn;
            console.error = self._originalConsole.error;
            console.info  = self._originalConsole.info;
            console.debug = self._originalConsole.debug;

            self._originalConsole = null;
            self._consoleCapturing = false;
        },

        /**
         * Returns captured console logs and clears the buffer.
         * @returns {Array<{timestamp: string, level: string, message: string}>}
         */
        getConsoleLogs: function () {
            var copy = this._consoleLogs.slice();
            this._consoleLogs = [];
            return copy;
        },

        // ── Global keyboard shortcuts ────────────────────────────────────

        _shortcutListener: null,

        /**
         * Registers a single global keydown listener that dispatches to .NET.
         * @param {object} dotNetRef  DotNetObjectReference with OnShortcut(string) method.
         */
        registerGlobalShortcutHandler: function (dotNetRef) {
            var self = this;

            if (self._shortcutListener) {
                return;
            }

            self._shortcutListener = function (e) {
                var parts = [];

                if (e.ctrlKey) { parts.push("ctrl"); }
                if (e.shiftKey) { parts.push("shift"); }
                if (e.altKey) { parts.push("alt"); }

                parts.push(e.key.toLowerCase());

                var shortcut = parts.join("+");

                dotNetRef.invokeMethodAsync("OnShortcut", shortcut).catch(function () {});
            };

            document.addEventListener("keydown", self._shortcutListener);
        },

        /**
         * Removes the global keydown listener.
         */
        unregisterGlobalShortcutHandler: function () {
            if (this._shortcutListener) {
                document.removeEventListener("keydown", this._shortcutListener);
                this._shortcutListener = null;
            }
        },
    };

    global.stratumUI.bugCapture = bugCapture;

})(window);
