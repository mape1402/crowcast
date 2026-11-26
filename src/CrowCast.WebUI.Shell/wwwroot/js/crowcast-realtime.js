(() => {
    const config = window.CrowCastRealtime;
    if (!config?.hubPath || !window.signalR) return;
    let stopped = false, connection;
    const state = value => document.querySelectorAll('[data-crowcast-live]').forEach(element => { element.dataset.state = value; element.textContent = value === 'connected' ? 'Live' : value; });
    const changed = () => document.dispatchEvent(new CustomEvent('crowcast:changed'));
    const start = async () => {
        connection = new signalR.HubConnectionBuilder().withUrl(config.hubPath).withAutomaticReconnect().build();
        connection.on('crowcast.changed', changed);
        connection.onreconnecting(() => state('reconnecting'));
        connection.onreconnected(async () => { state('connected'); await subscribe(); changed(); });
        connection.onclose(() => { state('offline'); if (!stopped) setTimeout(start, 3000); });
        try { await connection.start(); await subscribe(); state('connected'); } catch { state('offline'); if (!stopped) setTimeout(start, 3000); }
    };
    const subscribe = () => config.mode === 'fleet' ? connection.invoke('WatchFleet')
        : config.mode === 'cluster' ? connection.invoke('WatchCluster', config.clusterKey) : connection.invoke('Watch');
    document.addEventListener('DOMContentLoaded', start, { once: true });
    window.addEventListener('pagehide', () => { stopped = true; connection?.stop(); }, { once: true });
})();
