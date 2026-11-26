(() => {
    const controllers = new Map();
    const escapeText = value => value == null ? '' : String(value);
    const theme = root => {
        const css = getComputedStyle(root);
        const color = (name, fallback) => css.getPropertyValue(name).trim() || fallback;
        return agGrid.themeQuartz.withParams({
            accentColor: color('--z-primary', '#07988f'), backgroundColor: color('--z-surface', '#fff'),
            foregroundColor: color('--z-text', '#15251f'), borderColor: color('--z-border', '#d6e2dc'),
            fontSize: 11, spacing: 6
        });
    };
    const statusRenderer = params => {
        const span = document.createElement('span');
        const value = escapeText(params.value);
        span.className = `crowcast-status crowcast-status-${value.toLowerCase()}`;
        span.textContent = value;
        return span;
    };
    const dashboardRenderer = params => {
        const link = document.createElement('a');
        link.className = 'crowcast-dashboard-link';
        link.textContent = escapeText(params.value);
        link.href = params.data.url;
        link.setAttribute('aria-label', `View dashboard for ${escapeText(params.data.service)} ${escapeText(params.data.cluster)}`);
        return link;
    };
    const definitions = {
        members: [
            ['nodeId', 'Node ID', 1.4], ['generation', 'Generation', 1.2], ['status', 'Status', .7, statusRenderer],
            ['coordinator', 'Coordinator', .7], ['local', 'Local', .55], ['lastChangedAtUtc', 'Last changed', 1.15], ['endpoint', 'Endpoint', 1.4]
        ],
        clusterMembers: [
            ['nodeId', 'Node ID', 1.25], ['generation', 'Generation', 1.15], ['status', 'Status', .7, statusRenderer],
            ['coordinator', 'Coordinator', .7], ['seenBy', 'Seen by', .55], ['observers', 'Observers', .6],
            ['agreement', 'Status agreement', .9], ['lastChangedAtUtc', 'Last changed', 1.05], ['endpoint', 'Endpoint', 1.2]
        ],
        events: [
            ['occurredAtUtc', 'Occurred', 1], ['category', 'Category', .7], ['summary', 'Summary', 2],
            ['previousStatus', 'Previous', .7, statusRenderer], ['currentStatus', 'Current', .7, statusRenderer], ['messageType', 'Message type', 1]
        ],
        clusters: [
            ['dashboard', 'Dashboard', 1, dashboardRenderer],
            ['environment', 'Environment', .8], ['scope', 'Scope', .8], ['service', 'Service', 1.2], ['cluster', 'Cluster', 1],
            ['health', 'Health', 1, statusRenderer], ['freshObservers', 'Fresh', .55], ['observers', 'Total observers', .75],
            ['aliveMembers', 'Alive', .55], ['suspectMembers', 'Suspect', .6], ['deadMembers', 'Dead', .55], ['leftMembers', 'Left', .55],
            ['coordinatorCount', 'Coordinators', .7], ['agreement', 'Membership', .9], ['lastSeenAtUtc', 'Last report', 1.1]
        ],
        fleetClusters: [
            ['dashboard', 'Dashboard', 1, dashboardRenderer], ['service', 'Service', 1.2], ['cluster', 'Cluster', 1],
            ['health', 'Health', .9, statusRenderer], ['freshObservers', 'Fresh', .55], ['observers', 'Observers', .65],
            ['aliveMembers', 'Alive', .55], ['suspectMembers', 'Suspect', .6], ['deadMembers', 'Dead', .55], ['lastSeenAtUtc', 'Last report', 1.05]
        ],
        observers: [
            ['nodeId', 'Observer', 1.3], ['generation', 'Generation', 1.2], ['freshness', 'Freshness', .7, statusRenderer],
            ['connected', 'Connected', .65], ['members', 'Members', .6], ['coordinator', 'Coordinator', 1.3], ['lastSeenAtUtc', 'Last report', 1.1]
        ]
    };
    async function mount(root) {
        if (controllers.has(root) || !window.agGrid) return;
        const host = root.querySelector('[data-crowcast-grid-host]');
        const search = root.querySelector('[data-crowcast-grid-search]');
        const status = root.querySelector('[data-crowcast-grid-state]');
        const kind = root.dataset.crowcastGrid;
        const columns = (definitions[kind] || []).map(([field, headerName, flex, cellRenderer]) => ({
            field, headerName, flex, cellRenderer, minWidth: field === 'dashboard' ? 165 : 90,
            maxWidth: field === 'dashboard' ? 185 : undefined, sortable: field !== 'dashboard', resizable: true,
            filter: true
        }));
        const api = agGrid.createGrid(host, {
            theme: theme(root), columnDefs: columns, rowData: [], animateRows: false,
            defaultColDef: { sortable: true, resizable: true }, rowHeight: 42, headerHeight: 36,
            getRowId: params => String(params.data.id)
        });
        let request, signature;
        const refresh = async () => {
            request?.abort(); request = new AbortController();
            if (signature === undefined) status.textContent = 'Loading…';
            try {
                const response = await fetch(root.dataset.source, { signal: request.signal, headers: { Accept: 'application/json' } });
                if (!response.ok) throw new Error('Request failed');
                const body = await response.json();
                const rows = Array.isArray(body) ? body : body.items || [];
                const nextSignature = JSON.stringify(rows);
                if (signature !== nextSignature) {
                    api.setGridOption('rowData', rows);
                    signature = nextSignature;
                }
                const recordCount = `${rows.length.toLocaleString()} records`;
                if (status.textContent !== recordCount) status.textContent = recordCount;
            } catch (error) { if (error.name !== 'AbortError' && signature === undefined) status.textContent = 'Unable to load records.'; }
        };
        search?.addEventListener('input', () => api.setGridOption('quickFilterText', search.value));
        const observer = new MutationObserver(() => api.setGridOption('theme', theme(root)));
        observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-zoorce-theme', 'data-zoorce-theme-preset'] });
        root.addEventListener('crowcast:refresh', refresh);
        controllers.set(root, { refresh, dispose: () => { request?.abort(); observer.disconnect(); api.destroy(); } });
        await refresh();
    }
    const mountAll = scope => (scope || document).querySelectorAll('[data-crowcast-grid]').forEach(mount);
    document.addEventListener('DOMContentLoaded', () => mountAll(document), { once: true });
    document.addEventListener('crowcast:changed', () => controllers.forEach(controller => controller.refresh()));
    window.addEventListener('pagehide', () => controllers.forEach(controller => controller.dispose()), { once: true });
    window.CrowCastGrids = { mountAll };
})();
