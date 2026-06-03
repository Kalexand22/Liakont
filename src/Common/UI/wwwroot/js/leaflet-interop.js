// leaflet-interop.js — Leaflet.js interop module for StratumMap / StratumMapDraw
// Loaded as ES module via import().

const LEAFLET_VERSION = '1.9.4';
const LEAFLET_DRAW_VERSION = '1.0.4';

// ── Lazy-load Leaflet from CDN ──────────────────────────────────────────────

let leafletPromise = null;

function loadCss(href) {
    if (document.querySelector(`link[href="${href}"]`)) return;
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = href;
    document.head.appendChild(link);
}

function loadScript(src, integrity) {
    return new Promise((resolve, reject) => {
        if (document.querySelector(`script[src="${src}"]`)) {
            resolve();
            return;
        }
        const script = document.createElement('script');
        script.src = src;
        if (integrity) {
            script.integrity = integrity;
            script.crossOrigin = 'anonymous';
        }
        script.onload = resolve;
        script.onerror = () => reject(new Error(`Failed to load ${src}`));
        document.head.appendChild(script);
    });
}

// SRI hashes for CDN resources.
const LEAFLET_SRI = 'sha256-20nQCchB9co0qIjJZRGuk2/Z9VM+kNiyxNV1lvTlZBo=';
const LEAFLET_DRAW_SRI = 'sha256-gRMBqazCGeGJfGEJylVqzPi5MYIWpQ9mGMvluYGD2xM=';

function getLeaflet() {
    if (!leafletPromise) {
        leafletPromise = (async () => {
            if (window.L) return window.L;
            loadCss(`https://unpkg.com/leaflet@${LEAFLET_VERSION}/dist/leaflet.css`);
            await loadScript(
                `https://unpkg.com/leaflet@${LEAFLET_VERSION}/dist/leaflet.js`,
                LEAFLET_SRI);
            return window.L;
        })();
    }
    return leafletPromise;
}

let leafletDrawPromise = null;

function getLeafletDraw() {
    if (!leafletDrawPromise) {
        leafletDrawPromise = (async () => {
            const L = await getLeaflet();
            if (L.Draw) return L;
            loadCss(`https://unpkg.com/leaflet-draw@${LEAFLET_DRAW_VERSION}/dist/leaflet.draw.css`);
            await loadScript(
                `https://unpkg.com/leaflet-draw@${LEAFLET_DRAW_VERSION}/dist/leaflet.draw.js`,
                LEAFLET_DRAW_SRI);
            return L;
        })();
    }
    return leafletDrawPromise;
}

// ── Instance registry ───────────────────────────────────────────────────────

// containerId → { map, tileLayer, geoJsonLayer, markersLayer, wmsLayers, drawControl, drawnItems, resizeObserver }
const instances = new Map();

// ── Map lifecycle ───────────────────────────────────────────────────────────

/**
 * Initialize a Leaflet map in the specified container.
 * @param {string} containerId
 * @param {number} lat - Initial center latitude
 * @param {number} lng - Initial center longitude
 * @param {number} zoom - Initial zoom level
 * @param {string} tileUrl - Tile layer URL template
 * @param {string} attribution - Tile layer attribution
 */
export async function mapInit(containerId, lat, lng, zoom, tileUrl, attribution) {
    const L = await getLeaflet();
    const container = document.getElementById(containerId);
    if (!container) return;

    // Dispose existing if re-initialized.
    if (instances.has(containerId)) {
        mapDispose(containerId);
    }

    const map = L.map(container, {
        center: [lat, lng],
        zoom: zoom,
        zoomControl: true,
        attributionControl: true,
    });

    const tileLayer = L.tileLayer(tileUrl || 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: attribution || '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
        maxZoom: 19,
    }).addTo(map);

    const geoJsonLayer = L.layerGroup().addTo(map);
    const markersLayer = L.layerGroup().addTo(map);

    // ResizeObserver for responsive layout.
    const resizeObserver = new ResizeObserver(() => {
        map.invalidateSize();
    });
    resizeObserver.observe(container);

    instances.set(containerId, {
        map,
        tileLayer,
        geoJsonLayer,
        markersLayer,
        wmsLayers: new Map(),
        drawControl: null,
        drawnItems: null,
        resizeObserver,
    });
}

/**
 * Dispose the map instance and clean up resources.
 * @param {string} containerId
 */
export function mapDispose(containerId) {
    const inst = instances.get(containerId);
    if (!inst) return;

    if (inst.resizeObserver) inst.resizeObserver.disconnect();
    if (inst.drawControl) inst.map.removeControl(inst.drawControl);
    inst.map.remove();
    instances.delete(containerId);
}

/**
 * Set the map view to a new center and zoom.
 * @param {string} containerId
 * @param {number} lat
 * @param {number} lng
 * @param {number} zoom
 */
export async function mapSetView(containerId, lat, lng, zoom) {
    const inst = instances.get(containerId);
    if (!inst) return;
    inst.map.setView([lat, lng], zoom);
}

/**
 * Fit the map to the given bounds.
 * @param {string} containerId
 * @param {number} minLat
 * @param {number} minLng
 * @param {number} maxLat
 * @param {number} maxLng
 */
export async function mapFitBounds(containerId, minLat, minLng, maxLat, maxLng) {
    const inst = instances.get(containerId);
    if (!inst) return;
    inst.map.fitBounds([[minLat, minLng], [maxLat, maxLng]], { padding: [20, 20] });
}

// ── GeoJSON ─────────────────────────────────────────────────────────────────

/**
 * Display GeoJSON data on the map.
 * @param {string} containerId
 * @param {string} geoJsonString - Raw GeoJSON string (Feature or FeatureCollection)
 * @param {object} dotnetRef - DotNetObjectReference for callbacks
 */
export async function mapSetGeoJson(containerId, geoJsonString, dotnetRef) {
    const L = await getLeaflet();
    const inst = instances.get(containerId);
    if (!inst) return;

    inst.geoJsonLayer.clearLayers();

    if (!geoJsonString) return;

    let data;
    try {
        data = JSON.parse(geoJsonString);
    } catch {
        return;
    }

    const layer = L.geoJSON(data, {
        style: () => ({
            color: 'var(--color-primary-600, #2563eb)',
            weight: 2,
            fillOpacity: 0.2,
        }),
        pointToLayer: (feature, latlng) =>
            L.circleMarker(latlng, {
                radius: 6,
                fillColor: 'var(--color-primary-600, #2563eb)',
                color: '#fff',
                weight: 1,
                fillOpacity: 0.8,
            }),
        onEachFeature: (feature, layer) => {
            if (dotnetRef) {
                layer.on('click', () => {
                    const geojson = JSON.stringify(feature.geometry || feature);
                    dotnetRef.invokeMethodAsync('OnFeatureClicked', geojson);
                });
            }
            if (feature.properties) {
                const popup = Object.entries(feature.properties)
                    .filter(([, v]) => v != null)
                    .map(([k, v]) => `<strong>${escapeHtml(k)}:</strong> ${escapeHtml(v)}`)
                    .join('<br>');
                if (popup) layer.bindPopup(popup);
            }
        },
    });

    layer.addTo(inst.geoJsonLayer);
}

// ── Markers ─────────────────────────────────────────────────────────────────

/**
 * Set markers on the map.
 * @param {string} containerId
 * @param {Array<{latitude: number, longitude: number, label?: string, popupHtml?: string}>} markers
 * @param {object} [dotnetRef] - Optional DotNetObjectReference for marker click callbacks
 */
export async function mapSetMarkers(containerId, markers, dotnetRef) {
    const L = await getLeaflet();
    const inst = instances.get(containerId);
    if (!inst) return;

    inst.markersLayer.clearLayers();

    if (!markers || markers.length === 0) return;

    for (let i = 0; i < markers.length; i++) {
        const m = markers[i];
        const marker = L.marker([m.latitude, m.longitude]);
        if (m.label) marker.bindTooltip(m.label);
        if (m.popupHtml) marker.bindPopup(m.popupHtml);
        if (dotnetRef) {
            const idx = i;
            marker.on('click', async () => {
                try {
                    await dotnetRef.invokeMethodAsync('OnMarkerClickedJs', idx);
                } catch {
                    // Component disposed or circuit disconnected — ignore.
                }
            });
        }
        marker.addTo(inst.markersLayer);
    }
}

// ── WMS layers ──────────────────────────────────────────────────────────────

/**
 * Add or update a WMS overlay layer.
 * @param {string} containerId
 * @param {string} layerName
 * @param {string} url - WMS base URL
 * @param {string} layers - WMS layers param
 * @param {string} format
 * @param {boolean} transparent
 * @param {number} opacity
 */
export async function mapAddWmsLayer(containerId, layerName, url, layers, format, transparent, opacity) {
    const L = await getLeaflet();
    const inst = instances.get(containerId);
    if (!inst) return;

    // Remove existing layer with same name.
    if (inst.wmsLayers.has(layerName)) {
        inst.map.removeLayer(inst.wmsLayers.get(layerName));
    }

    const wmsLayer = L.tileLayer.wms(url, {
        layers: layers,
        format: format || 'image/png',
        transparent: transparent !== false,
        opacity: opacity ?? 1.0,
    }).addTo(inst.map);

    inst.wmsLayers.set(layerName, wmsLayer);
}

/**
 * Remove all WMS layers.
 * @param {string} containerId
 */
export function mapClearWmsLayers(containerId) {
    const inst = instances.get(containerId);
    if (!inst) return;

    for (const [name, layer] of inst.wmsLayers) {
        inst.map.removeLayer(layer);
    }
    inst.wmsLayers.clear();
}

/**
 * Remove a WMS layer by name.
 * @param {string} containerId
 * @param {string} layerName
 */
export function mapRemoveWmsLayer(containerId, layerName) {
    const inst = instances.get(containerId);
    if (!inst) return;

    const layer = inst.wmsLayers.get(layerName);
    if (layer) {
        inst.map.removeLayer(layer);
        inst.wmsLayers.delete(layerName);
    }
}

// ── Drawing ─────────────────────────────────────────────────────────────────

/**
 * Enable drawing tools on the map.
 * @param {string} containerId
 * @param {string} mode - 'polygon' | 'line' | 'point' | 'rectangle'
 * @param {string|null} existingGeoJson - Existing geometry to edit
 * @param {object} dotnetRef - DotNetObjectReference for OnDrawComplete callback
 */
export async function mapEnableDraw(containerId, mode, existingGeoJson, dotnetRef) {
    const L = await getLeafletDraw();
    const inst = instances.get(containerId);
    if (!inst) return;

    // Clean up previous draw controls and event listeners.
    inst.map.off(L.Draw.Event.CREATED);
    inst.map.off(L.Draw.Event.EDITED);
    inst.map.off(L.Draw.Event.DELETED);

    if (inst.drawControl) {
        inst.map.removeControl(inst.drawControl);
        inst.drawControl = null;
    }
    if (inst.drawnItems) {
        inst.map.removeLayer(inst.drawnItems);
    }

    const drawnItems = new L.FeatureGroup();
    inst.map.addLayer(drawnItems);
    inst.drawnItems = drawnItems;

    // Load existing geometry if provided.
    if (existingGeoJson) {
        try {
            const data = JSON.parse(existingGeoJson);
            L.geoJSON(data).eachLayer(layer => {
                drawnItems.addLayer(layer);
            });
        } catch { /* ignore parse errors */ }
    }

    // Configure draw options based on mode.
    const drawOptions = {
        polygon: false,
        polyline: false,
        circle: false,
        circlemarker: false,
        marker: false,
        rectangle: false,
    };

    switch (mode) {
        case 'polygon':
            drawOptions.polygon = {
                shapeOptions: { color: 'var(--color-primary-600, #2563eb)' },
            };
            break;
        case 'line':
            drawOptions.polyline = {
                shapeOptions: { color: 'var(--color-primary-600, #2563eb)' },
            };
            break;
        case 'point':
            drawOptions.marker = true;
            break;
        case 'rectangle':
            drawOptions.rectangle = {
                shapeOptions: { color: 'var(--color-primary-600, #2563eb)' },
            };
            break;
    }

    const drawControl = new L.Control.Draw({
        draw: drawOptions,
        edit: {
            featureGroup: drawnItems,
            remove: true,
        },
    });

    inst.map.addControl(drawControl);
    inst.drawControl = drawControl;

    // Listen for draw events.
    inst.map.on(L.Draw.Event.CREATED, (e) => {
        drawnItems.addLayer(e.layer);
        notifyGeometryChange(containerId, dotnetRef);
    });

    inst.map.on(L.Draw.Event.EDITED, () => {
        notifyGeometryChange(containerId, dotnetRef);
    });

    inst.map.on(L.Draw.Event.DELETED, () => {
        notifyGeometryChange(containerId, dotnetRef);
    });
}

/**
 * Disable drawing tools.
 * @param {string} containerId
 */
export function mapDisableDraw(containerId) {
    const inst = instances.get(containerId);
    if (!inst) return;

    if (inst.drawControl) {
        inst.map.removeControl(inst.drawControl);
        inst.drawControl = null;
    }
}

/**
 * Clear all drawn items from the map.
 * @param {string} containerId
 * @param {object} dotnetRef
 */
export function mapClearDrawn(containerId, dotnetRef) {
    const inst = instances.get(containerId);
    if (!inst || !inst.drawnItems) return;
    inst.drawnItems.clearLayers();
    if (dotnetRef) {
        notifyGeometryChange(containerId, dotnetRef);
    }
}

/**
 * Get the current drawn geometry as GeoJSON.
 * @param {string} containerId
 * @returns {string|null} GeoJSON string or null
 */
export function mapGetDrawnGeoJson(containerId) {
    const inst = instances.get(containerId);
    if (!inst || !inst.drawnItems) return null;

    const layers = [];
    inst.drawnItems.eachLayer(layer => {
        layers.push(layer.toGeoJSON());
    });

    if (layers.length === 0) return null;
    if (layers.length === 1) return JSON.stringify(layers[0].geometry);

    return JSON.stringify({
        type: 'GeometryCollection',
        geometries: layers.map(f => f.geometry),
    });
}

// ── Internal helpers ────────────────────────────────────────────────────────

function escapeHtml(str) {
    if (str == null) return '';
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
}

function notifyGeometryChange(containerId, dotnetRef) {
    if (!dotnetRef) return;
    const geoJson = mapGetDrawnGeoJson(containerId);
    dotnetRef.invokeMethodAsync('OnDrawCompleteJs', geoJson || '');
}
