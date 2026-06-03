// Chart.js — ECharts interop module for Stratum Chart<TData>
// Loaded as ES module via import().

// Lazy-load ECharts from vendored file.
let echartsPromise = null;
function getECharts() {
    if (!echartsPromise) {
        echartsPromise = new Promise((resolve, reject) => {
            // Check if echarts is already loaded globally.
            if (window.echarts) {
                resolve(window.echarts);
                return;
            }
            const script = document.createElement('script');
            script.src = '_content/Stratum.Common.UI/lib/echarts.min.js';
            script.onload = () => resolve(window.echarts);
            script.onerror = () => reject(new Error('Failed to load ECharts'));
            document.head.appendChild(script);
        });
    }
    return echartsPromise;
}

// Instance registry: containerId → echarts instance.
const instances = new Map();

/**
 * Initialize a chart in the specified container.
 * @param {string} containerId - DOM id of the container element.
 * @param {object} options - ECharts option object.
 */
export async function chartInit(containerId, options) {
    const ec = await getECharts();
    const container = document.getElementById(containerId);
    if (!container) return;

    // Dispose existing instance if any.
    if (instances.has(containerId)) {
        instances.get(containerId).dispose();
    }

    const chart = ec.init(container);
    // Clean null values from options to avoid ECharts warnings.
    const cleaned = cleanNulls(options);
    chart.setOption(cleaned);
    instances.set(containerId, chart);

    // Auto-resize on window resize.
    const resizeHandler = () => chart.resize();
    window.addEventListener('resize', resizeHandler);
    chart._stratumResizeHandler = resizeHandler;
}

/**
 * Update an existing chart with new options.
 * @param {string} containerId
 * @param {object} options
 */
export async function chartUpdate(containerId, options) {
    const chart = instances.get(containerId);
    if (!chart) {
        // Not initialized yet; init instead.
        await chartInit(containerId, options);
        return;
    }
    const cleaned = cleanNulls(options);
    chart.setOption(cleaned, { notMerge: true });
}

/**
 * Dispose the chart instance.
 * @param {string} containerId
 */
export function chartDispose(containerId) {
    const chart = instances.get(containerId);
    if (chart) {
        if (chart._stratumResizeHandler) {
            window.removeEventListener('resize', chart._stratumResizeHandler);
        }
        chart.dispose();
        instances.delete(containerId);
    }
}

/**
 * Register a click handler that calls back to .NET.
 * @param {string} containerId
 * @param {object} dotnetRef - DotNetObjectReference
 */
export function chartRegisterClickHandler(containerId, dotnetRef) {
    // Called after chartInit — instance should already exist.
    const chart = instances.get(containerId);
    if (!chart) return;

    chart.on('click', function (params) {
        if (params.componentType === 'series') {
            // Normalize value: radar charts return arrays, pie returns objects.
            var val = params.value;
            if (Array.isArray(val)) {
                val = val.length > 0 ? val[0] : 0;
            } else if (typeof val === 'object' && val !== null) {
                val = 0;
            }
            val = typeof val === 'number' ? val : 0;

            dotnetRef.invokeMethodAsync(
                'OnChartPointClick',
                params.seriesName || '',
                params.name || '',
                val,
                params.dataIndex || 0
            );
        }
    });
}

/**
 * Remove null/undefined properties recursively to avoid ECharts warnings.
 */
function cleanNulls(obj) {
    if (obj === null || obj === undefined) return undefined;
    if (Array.isArray(obj)) return obj.map(cleanNulls).filter(x => x !== undefined);
    if (typeof obj !== 'object') return obj;
    const result = {};
    for (const [key, value] of Object.entries(obj)) {
        const cleaned = cleanNulls(value);
        if (cleaned !== undefined) {
            result[key] = cleaned;
        }
    }
    return result;
}
