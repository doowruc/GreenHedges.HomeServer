// Draws insurance costs over time in <canvas data-premium-chart='[{"vehicle":"Ford Focus (AB12 CDE)","points":[{"start":
// "yyyy-MM-ddTHH:mm","end":"…","provider":"…","cost":450.00,"yearly":452.10,"days":364,"policyId":3}, …]}, …]'>
// (Services/InsuranceCosts.ChartJson): one line per vehicle, each point its time on a policy, at its start, on a linear
// date axis so the gaps show to scale. "cost" is the vehicle's share of the policy (by time on it) plus its adjustment.
// Buttons with data-premium-view="actual|yearly" switch between that and the same rate for a year (so a few months on a
// policy compares with the rest); "actual" is shown first. Clicking a point opens its policy.
// Needs Chart.js first. A chart in a hidden tab is sized when the tab is shown (Chart.js watches the canvas's size).
(() => {
    'use strict';
    const canvas = document.querySelector('canvas[data-premium-chart]');
    if (!canvas) return;

    // The times are UK wall-clock times; read and shown as UTC so the browser's own time zone doesn't move them.
    const parse = text => Date.parse(text + 'Z');
    const formatDate = (ms, options) => new Date(ms).toLocaleDateString('en-GB', { timeZone: 'UTC', ...options });
    const money = v => v.toLocaleString('en-GB', { style: 'currency', currency: 'GBP' });
    const msPerMonth = 365.25 / 12 * 86400000;
    // Bootstrap's green, blue, orange, purple, teal, pink: distinct in both themes.
    const colours = ['#198754', '#0d6efd', '#fd7e14', '#6f42c1', '#20c997', '#d63384'];

    const styleDefaults = () => {
        const { text, grid } = window.themeColors();
        Chart.defaults.color = text;
        Chart.defaults.borderColor = grid;
    };
    styleDefaults();

    const series = JSON.parse(canvas.dataset.premiumChart);
    let view = 'actual';
    const data = s => s.points.map(p => ({ x: parse(p.start), y: view === 'yearly' ? p.yearly : p.cost }));

    const starts = series.flatMap(s => s.points.map(p => parse(p.start)));
    const first = Math.min(...starts), last = Math.max(...starts);
    const pad = Math.max((last - first) * 0.03, 14 * 86400000);
    const chart = new Chart(canvas, {
        type: 'line',
        data: {
            datasets: series.map((s, i) => {
                const colour = colours[i % colours.length];
                return { label: s.vehicle, data: data(s), borderColor: colour, backgroundColor: colour, pointRadius: 3, pointHoverRadius: 5 };
            })
        },
        options: {
            // Fill .chart-box's height rather than a fixed width:height ratio, which left it very short on phones.
            maintainAspectRatio: false,
            interaction: { mode: 'nearest', intersect: false },
            onClick: (e, elements) => {
                if (elements.length === 0) return;
                const point = series[elements[0].datasetIndex].points[elements[0].index];
                location.href = `/Insurance/Details/${point.policyId}`;
            },
            onHover: (e, elements) => { e.native.target.style.cursor = elements.length > 0 ? 'pointer' : 'default'; },
            plugins: {
                // One vehicle needs no key.
                legend: { display: series.length > 1 },
                tooltip: {
                    callbacks: {
                        title: items => {
                            if (items.length === 0) return '';
                            const p = series[items[0].datasetIndex].points[items[0].dataIndex], day = { day: '2-digit', month: '2-digit', year: 'numeric' };
                            return `${formatDate(parse(p.start), day)} – ${formatDate(parse(p.end), day)}`;
                        },
                        label: item => `${series[item.datasetIndex].vehicle}: ${money(item.parsed.y)}${view === 'yearly' ? ' a year' : ''}`,
                        afterLabel: item => {
                            const p = series[item.datasetIndex].points[item.dataIndex];
                            return [p.provider, view === 'yearly' ? `${money(p.cost)} for ${p.days} days` : null].filter(Boolean).join(' · ');
                        }
                    }
                }
            },
            scales: {
                x: {
                    type: 'linear',
                    min: first - pad,
                    max: last + pad,
                    ticks: {
                        callback(v) {
                            const short = this.max - this.min < 4 * msPerMonth;
                            return formatDate(v, short ? { day: 'numeric', month: 'short' } : { month: 'short', year: 'numeric' });
                        },
                        maxRotation: 0,
                        autoSkipPadding: 16
                    }
                },
                y: {
                    title: { display: true, text: 'Cost (£)' },
                    beginAtZero: true,
                    grace: '5%',
                    ticks: { callback: v => money(v) }
                }
            }
        }
    });

    document.querySelectorAll('[data-premium-view]').forEach(button => button.addEventListener('click', () => {
        view = button.dataset.premiumView;
        document.querySelectorAll('[data-premium-view]').forEach(b => {
            const on = b.dataset.premiumView === view;
            b.classList.toggle('active', on);
            b.setAttribute('aria-pressed', on ? 'true' : 'false');
        });
        chart.data.datasets.forEach((d, i) => { d.data = data(series[i]); });
        chart.update();
    }));

    document.addEventListener('themechange', () => {
        styleDefaults();
        // Scales copy the defaults when created, so update them directly too.
        const { text, grid } = window.themeColors();
        for (const scale of Object.values(chart.options.scales)) {
            scale.ticks.color = text;
            scale.title.color = text;
            scale.grid.color = grid;
            scale.border.color = grid;
        }
        chart.options.plugins.legend.labels.color = text;
        chart.update('none');
    });
})();
