// The pay charts (Pages/PayCharts.cs builds their data). Needs Chart.js first.
// <canvas data-pay-yearly='{"labels":["2025-26",…],"gross":[…],"net":[…],"pension":[…]}'>: bars per tax year.
// <canvas data-pay-monthly='{"labels":["Apr 25",…],"gross":[…],"net":[…]}'>: lines per payslip.
// <canvas data-pension-values='{"series":[{"label":"SIPP","points":[{"date":"2026-04-05","value":1000.5},…]},…]}'>: a line
// per pension, its points spaced by date (statements aren't evenly spaced, and pensions' dates differ).
(() => {
    'use strict';
    const colours = { gross: '#0d6efd', net: '#198754', pension: '#fd7e14' };
    const pounds = new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP', maximumFractionDigits: 0 });
    const pence = new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP' });
    const seriesColours = ['#0d6efd', '#fd7e14', '#198754', '#6f42c1', '#d63384', '#20c997'];
    const dated = (ms, format) => new Date(ms).toLocaleDateString('en-GB', { ...format, timeZone: 'UTC' });

    const styleDefaults = () => {
        const { text, grid } = window.themeColors();
        Chart.defaults.color = text;
        Chart.defaults.borderColor = grid;
    };
    styleDefaults();

    const options = {
        // Fill .chart-box's height, so the chart stays readable on phones.
        maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        plugins: { tooltip: { callbacks: { label: item => `${item.dataset.label}: ${pence.format(item.parsed.y)}` } } },
        scales: { x: { grid: { display: false } }, y: { beginAtZero: true, ticks: { callback: v => pounds.format(v) } } }
    };

    const charts = [
        ...[...document.querySelectorAll('canvas[data-pay-yearly]')].map(canvas => {
            const d = JSON.parse(canvas.dataset.payYearly);
            return new Chart(canvas, {
                type: 'bar',
                data: {
                    labels: d.labels,
                    datasets: [
                        { label: 'Gross', data: d.gross, backgroundColor: colours.gross, borderRadius: 2 },
                        { label: 'Net', data: d.net, backgroundColor: colours.net, borderRadius: 2 },
                        { label: 'Pension (yours)', data: d.pension, backgroundColor: colours.pension, borderRadius: 2 }
                    ]
                },
                options
            });
        }),
        ...[...document.querySelectorAll('canvas[data-pay-monthly]')].map(canvas => {
            const d = JSON.parse(canvas.dataset.payMonthly);
            const line = (label, data, colour) => ({
                label, data, borderColor: colour, backgroundColor: colour, borderWidth: 2, pointRadius: 0, pointHoverRadius: 4, tension: .1
            });
            return new Chart(canvas, {
                type: 'line',
                data: { labels: d.labels, datasets: [line('Gross', d.gross, colours.gross), line('Net', d.net, colours.net)] },
                options: { ...options, scales: { ...options.scales, x: { grid: { display: false }, ticks: { maxTicksLimit: 12 } } } }
            });
        }),
        ...[...document.querySelectorAll('canvas[data-pension-values]')].map(canvas => {
            const d = JSON.parse(canvas.dataset.pensionValues);
            return new Chart(canvas, {
                type: 'line',
                data: {
                    datasets: d.series.map((s, i) => {
                        const colour = seriesColours[i % seriesColours.length];
                        return {
                            label: s.label, data: s.points.map(p => ({ x: Date.parse(p.date), y: p.value })),
                            borderColor: colour, backgroundColor: colour, borderWidth: 2, pointRadius: 3, pointHoverRadius: 5, tension: .1
                        };
                    })
                },
                options: {
                    ...options,
                    interaction: { mode: 'nearest', axis: 'x', intersect: false },
                    plugins: {
                        tooltip: {
                            callbacks: {
                                ...options.plugins.tooltip.callbacks,
                                title: items => dated(items[0].parsed.x, { day: 'numeric', month: 'short', year: 'numeric' })
                            }
                        }
                    },
                    scales: {
                        ...options.scales,
                        x: { type: 'linear', grid: { display: false }, ticks: { maxTicksLimit: 8, callback: v => dated(v, { month: 'short', year: '2-digit' }) } }
                    }
                }
            });
        })
    ];

    // Follow the light/dark theme (color-mode.js).
    document.addEventListener('themechange', () => {
        styleDefaults();
        charts.forEach(chart => chart.update());
    });
})();
