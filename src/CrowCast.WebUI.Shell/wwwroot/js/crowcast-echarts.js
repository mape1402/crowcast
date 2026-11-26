(() => {
    const controllers = new Map();
    const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, character => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    })[character]);
    const palette = root => {
        const css = getComputedStyle(root);
        const value = (name, fallback) => css.getPropertyValue(name).trim() || fallback;
        return { text: value('--z-text', '#15251f'), muted: value('--z-text-muted', '#66766f'), border: value('--z-border', '#d6e2dc'),
            surface: value('--z-surface', '#fff'), primary: value('--z-primary', '#07988f'), primaryForeground: value('--z-primary-foreground', '#fff'),
            alive: '#13a38f', suspect: '#e0a128', dead: '#dc5268', left: '#72839a' };
    };
    function option(kind, data, colors) {
        const base = { animationDuration: 250, animationDurationUpdate: 0, textStyle: { color: colors.text }, aria: { enabled: true }, tooltip: { trigger: 'item' } };
        if (kind === 'membership') {
            const samples = data.samples || [];
            return { ...base, color: [colors.alive, colors.suspect, colors.dead, colors.left], tooltip: { trigger: 'axis' }, legend: { textStyle: { color: colors.muted } },
                grid: { left: 35, right: 12, top: 35, bottom: 30 }, xAxis: { type: 'category', data: samples.map(x => new Date(x.observedAtUtc).toLocaleTimeString()), axisLabel: { color: colors.muted } },
                yAxis: { type: 'value', minInterval: 1, splitLine: { lineStyle: { color: colors.border } }, axisLabel: { color: colors.muted } },
                series: ['alive','suspect','dead','left'].map(name => ({ name: name[0].toUpperCase() + name.slice(1), type: 'line', stack: 'members', areaStyle: {}, symbol: 'none', data: samples.map(x => x[name]) })) };
        }
        if (kind === 'messages') {
            const samples = data.samples || [];
            const series = [['Published','messagesPublished',colors.primary],['Received','messagesReceived',colors.alive],['Duplicates','duplicateMessages',colors.suspect],['Rejected','rejectedMessages',colors.dead]];
            return { ...base, tooltip: { trigger: 'axis' }, legend: { textStyle: { color: colors.muted } }, grid: { left: 48, right: 12, top: 35, bottom: 30 },
                xAxis: { type: 'category', data: samples.map(x => new Date(x.observedAtUtc).toLocaleTimeString()), axisLabel: { color: colors.muted } },
                yAxis: { type: 'value', splitLine: { lineStyle: { color: colors.border } }, axisLabel: { color: colors.muted } },
                series: series.map(([name,key,color]) => ({ name, type: 'line', smooth: .25, symbol: 'none', itemStyle: { color }, data: samples.map(x => x.counters[key]) })) };
        }
        if (kind === 'health') {
            const rows = data.items || data || [];
            const groups = Object.entries(rows.reduce((result, row) => { result[row.health] = (result[row.health] || 0) + 1; return result; }, {}));
            const healthColors = { Healthy: colors.alive, Degraded: colors.suspect, Offline: colors.dead, PartitionSuspected: colors.dead, CoordinatorDisagreement: colors.dead };
            return { ...base, legend: { bottom: 0, textStyle: { color: colors.muted } }, graphic: [{ type: 'text', left: 'center', top: 'middle', style: { text: `${rows.length}\nclusters`, textAlign: 'center', fill: colors.text, fontSize: 14, lineHeight: 20 } }],
                series: [{ type: 'pie', radius: ['48%','72%'], label: { color: colors.text }, data: groups.map(([name,value]) => ({ name, value, itemStyle: { color: healthColors[name] || colors.primary } })) }] };
        }
        if (kind === 'fleet-members') {
            const rows = data.items || data || [];
            const series = [['Alive','aliveMembers',colors.alive],['Suspect','suspectMembers',colors.suspect],['Dead','deadMembers',colors.dead],['Left','leftMembers',colors.left]];
            return { ...base, tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } }, legend: { top: 0, textStyle: { color: colors.muted } },
                grid: { left: 125, right: 18, top: 40, bottom: 24 }, xAxis: { type: 'value', minInterval: 1, splitLine: { lineStyle: { color: colors.border } }, axisLabel: { color: colors.muted } },
                yAxis: { type: 'category', inverse: true, data: rows.map(row => `${row.service}/${row.cluster}`), axisLabel: { color: colors.muted, width: 112, overflow: 'truncate' } },
                series: series.map(([name,key,color]) => ({ name, type: 'bar', stack: 'members', barMaxWidth: 24, itemStyle: { color }, data: rows.map(row => row[key] || 0) })) };
        }
        if (kind === 'counters') {
            const counters = data.counters || {};
            const rows = [
                ['Published', counters.messagesPublished || 0, colors.primary],
                ['Received', counters.messagesReceived || 0, colors.alive],
                ['Duplicates', counters.duplicateMessages || 0, colors.suspect],
                ['Rejected', counters.rejectedMessages || 0, colors.dead],
                ['Rate limited', counters.rateLimitedMessages || 0, colors.left]
            ];
            if (rows.every(row => row[1] === 0)) {
                return { ...base, graphic: [{ type: 'text', left: 'center', top: 'middle', style: { text: 'No message activity observed yet', fill: colors.muted, fontSize: 14 } }] };
            }
            return { ...base, grid: { left: 92, right: 20, top: 10, bottom: 24 }, xAxis: { type: 'value', minInterval: 1, splitLine: { lineStyle: { color: colors.border } }, axisLabel: { color: colors.muted } },
                yAxis: { type: 'category', inverse: true, data: rows.map(row => row[0]), axisLabel: { color: colors.muted } },
                series: [{ type: 'bar', barMaxWidth: 22, data: rows.map(row => ({ value: row[1], itemStyle: { color: row[2], borderRadius: [0, 4, 4, 0] } })), label: { show: true, position: 'right', color: colors.text } }] };
        }
        if (kind === 'agreement') {
            const observers = data.observers || [];
            const memberIds = [...new Set(observers.flatMap(observer => (observer.snapshot?.members || []).map(member => member.identity.nodeId)))];
            const values = { Alive: 3, Suspect: 2, Dead: 1, Left: 0 }, statuses = ['Left', 'Dead', 'Suspect', 'Alive'];
            const statusColors = [colors.left, colors.dead, colors.suspect, colors.alive], points = [];
            observers.forEach((observer, y) => (observer.snapshot?.members || []).forEach(member => {
                const x = memberIds.indexOf(member.identity.nodeId); if (x >= 0) points.push([x, y, values[member.status] ?? 0]);
            }));
            return { ...base, tooltip: { backgroundColor: colors.surface, borderColor: colors.border, textStyle: { color: colors.text }, formatter: p => {
                    const observer = observers[p.data[1]]?.identity?.nodeId || 'Unknown';
                    const member = memberIds[p.data[0]] || 'Unknown';
                    const status = statuses[p.data[2]] || 'Unknown';
                    return `<strong>Observer</strong>: ${escapeHtml(observer)}<br><strong>Member</strong>: ${escapeHtml(member)}<br><strong>Reported status</strong>: ${escapeHtml(status)}`;
                } },
                grid: { left: 80, right: 18, top: 18, bottom: 50 }, xAxis: { type: 'category', data: memberIds.map(id => id.slice(0, 8)), axisLabel: { color: colors.muted, rotate: 35 } },
                yAxis: { type: 'category', data: observers.map(x => x.identity.nodeId.slice(0, 8)), axisLabel: { color: colors.muted } },
                series: [{ type: 'scatter', symbol: 'rect', symbolSize: [34, 26], data: points,
                    label: { show: true, formatter: params => statuses[params.data[2]][0], color: colors.surface, fontWeight: 700 },
                    itemStyle: { color: params => statusColors[params.data[2]], borderColor: colors.surface, borderWidth: 2 } }] };
        }
        const members = (data.members || []).slice().sort((left, right) => (left.identity.nodeId + left.identity.generation).localeCompare(right.identity.nodeId + right.identity.generation));
        const coordinator = data.coordinator ? `${data.coordinator.nodeId}/${data.coordinator.generation}` : null;
        const memberId = member => `${member.identity.nodeId}/${member.identity.generation}`;
        const statusColors = { Alive: colors.alive, Suspect: colors.suspect, Dead: colors.dead, Left: colors.left };
        const labelColors = { Alive: '#ffffff', Suspect: '#3a2a00', Dead: '#ffffff', Left: '#ffffff' };
        return { ...base, tooltip: { trigger: 'item', backgroundColor: colors.surface, borderColor: colors.border, textStyle: { color: colors.text }, formatter: params => {
                if (params.dataType === 'edge') {
                    return `<strong>Membership link</strong><br>${escapeHtml(params.data.source)} → ${escapeHtml(params.data.target)}`;
                }
                const node = params.data;
                const endpoint = node.endpoint ? `<br><strong>Endpoint</strong>: ${escapeHtml(node.endpoint)}` : '';
                return `<strong>${escapeHtml(node.nodeId)}</strong><br><strong>Status</strong>: ${escapeHtml(node.status)}<br><strong>Generation</strong>: ${escapeHtml(node.generation)}<br><strong>Role</strong>: ${escapeHtml(node.role)}${endpoint}`;
            } },
            series: [{ type: 'graph', layout: 'circular', circular: { rotateLabel: false }, roam: true,
            label: { show: true, formatter: p => p.data.displayLabel, fontSize: 8, fontWeight: 600, lineHeight: 9 }, lineStyle: { color: colors.border, width: 1.5 },
            data: members.map(member => {
                const id = memberId(member), isCoordinator = id === coordinator;
                const displayLabel = member.identity.nodeId.slice(0, 6);
                return { id, name: displayLabel, value: member.status,
                    nodeId: member.identity.nodeId, generation: member.identity.generation, status: member.status,
                    role: isCoordinator ? 'Coordinator' : 'Member', endpoint: member.endpoint || '',
                    displayLabel,
                    symbolSize: isCoordinator ? 72 : 54,
                    label: { show: true, formatter: displayLabel,
                        color: labelColors[member.status] || colors.primaryForeground, fontSize: 8, fontWeight: 600, lineHeight: 9 },
                    itemStyle: { color: statusColors[member.status] || colors.primary } };
            }),
            links: coordinator ? members.filter(member => memberId(member) !== coordinator).map(member => ({ source: coordinator, target: memberId(member) })) : [] }] };
    }
    async function mount(root) {
        if (controllers.has(root) || !window.echarts) return;
        const chart = echarts.init(root);
        let request, data, signature;
        const render = () => {
            const next = option(root.dataset.crowcastChart, data, palette(root));
            const nextSignature = JSON.stringify(next);
            if (signature === nextSignature) return;
            chart.setOption(next, true);
            signature = nextSignature;
        };
        const refresh = async () => {
            request?.abort(); request = new AbortController();
            try { const response = await fetch(root.dataset.source, { signal: request.signal }); if (!response.ok) throw new Error(); data = await response.json(); render(); }
            catch (error) { if (error.name !== 'AbortError') root.textContent = 'Chart data is unavailable.'; }
        };
        const resize = new ResizeObserver(() => chart.resize()); resize.observe(root);
        const themes = new MutationObserver(() => { if (data) { signature = undefined; render(); } });
        themes.observe(document.documentElement, { attributes: true, attributeFilter: ['data-zoorce-theme','data-zoorce-theme-preset'] });
        controllers.set(root, { refresh, dispose: () => { request?.abort(); resize.disconnect(); themes.disconnect(); chart.dispose(); } });
        await refresh();
    }
    document.addEventListener('DOMContentLoaded', () => document.querySelectorAll('[data-crowcast-chart]').forEach(mount), { once: true });
    document.addEventListener('crowcast:changed', () => controllers.forEach(controller => controller.refresh()));
    window.addEventListener('pagehide', () => controllers.forEach(controller => controller.dispose()), { once: true });
})();
