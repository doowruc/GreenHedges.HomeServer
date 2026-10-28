// The pay charts (Pages/PayCharts.cs builds their data). Needs Chart.js first.
// <canvas data-pay-yearly='{"labels":["2025-26",…],"gross":[…],"net":[…],"pension":[…]}'>: bars per tax year.
// <canvas data-pay-monthly='{"labels":["Apr 25",…],"gross":[…],"net":[…]}'>: lines per payslip.
(() => {
    'use strict';
    const colours = { gross: '#0d6efd', net: '#198754', pension: '#fd7e14' };
    const pounds = new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP', maximumFractionDigits: 0 });
    const pence = new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP' });

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
        })
    ];

    // Follow the light/dark theme (color-mode.js).
    document.addEventListener('themechange', () => {
        styleDefaults();
        charts.forEach(chart => chart.update());
    });
})();
