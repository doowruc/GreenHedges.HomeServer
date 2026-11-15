// A utility's chart (Services/UtilityChart.cs builds its data). Needs Chart.js first.
// <canvas data-utility-chart='{"unit":"kWh","decimals":2,"points":[{"date":"2026-04-05","value":12.5},…],"trendOrder":4,"trend":[…]}'>:
// units a day at each reading's date, spaced by date (readings aren't evenly spaced), with the polynomial trendline.
(() => {
    'use strict';
    const colours = { used: '#0d6efd', trend: '#fd7e14' };
    const dated = (ms, format) => new Date(ms).toLocaleDateString('en-GB', { ...format, timeZone: 'UTC' });
    const xy = points => points.map(p => ({ x: Date.parse(p.date), y: p.value }));

    const styleDefaults = () => {
        const { text, grid } = window.themeColors();
        Chart.defaults.color = text;
        Chart.defaults.borderColor = grid;
    };
    styleDefaults();

    const charts = [...document.querySelectorAll('canvas[data-utility-chart]')].map(canvas => {
        const d = JSON.parse(canvas.dataset.utilityChart);
        const number = new Intl.NumberFormat('en-GB', { minimumFractionDigits: d.decimals, maximumFractionDigits: d.decimals });
        const axisNumber = new Intl.NumberFormat('en-GB', { maximumFractionDigits: d.decimals });
        const points = xy(d.points);
        const [first, last] = [points[0].x, points[points.length - 1].x];
        // 1 January of each year the readings span, after the first reading.
        const years = [];
        for (let year = new Date(first).getUTCFullYear() + 1; Date.UTC(year, 0, 1) <= last; year++) years.push(Date.UTC(year, 0, 1));
        const datasets = [{
            label: `${d.unit} a day`, data: points,
            borderColor: colours.used, backgroundColor: colours.used, borderWidth: 2, pointRadius: 2, pointHoverRadius: 5, tension: 0
        }];
        if (d.trend.length > 0)
            datasets.push({
                label: `Trend (polynomial, order ${d.trendOrder})`, data: xy(d.trend),
                borderColor: colours.trend, backgroundColor: colours.trend, borderWidth: 2, pointRadius: 0, pointHoverRadius: 0, pointHitRadius: 0, tension: 0
            });

        return new Chart(canvas, {
            type: 'line',
            data: { datasets },
            options: {
                // Fill .chart-box's height, so the chart stays readable on phones.
                maintainAspectRatio: false,
                interaction: { mode: 'nearest', axis: 'x', intersect: false },
                plugins: {
                    tooltip: {
                        // The readings only: the trendline has a point every few days, which would crowd them out.
                        filter: item => item.datasetIndex === 0,
                        callbacks: {
                            title: items => items.length ? dated(items[0].parsed.x, { day: 'numeric', month: 'short', year: 'numeric' }) : '',
                            label: item => `${number.format(item.parsed.y)} ${d.unit} a day`
                        }
                    }
                },
                scales: {
                    x: {
                        type: 'linear', min: first, max: last, grid: { display: false },
                        // With a few years of readings, a tick at the start of each year; otherwise wherever Chart.js puts them.
                        afterBuildTicks: axis => { if (years.length >= 2) axis.ticks = years.map(value => ({ value })); },
                        ticks: { maxTicksLimit: 12, callback: v => dated(v, years.length >= 2 ? { year: 'numeric' } : { month: 'short', year: '2-digit' }) }
                    },
                    y: { beginAtZero: true, ticks: { callback: v => axisNumber.format(v) }, title: { display: true, text: `${d.unit} a day` } }
                }
            }
        });
    });

    // <canvas data-by-month-chart='{"unit":"m³","decimals":2,"years":[{"year":2025,"values":[12.5,null,…12 of them]},…]}'>:
    // units a day in each month of the year, a line per year, to compare the same month across the years. The latest
    // year stands out; the ones before it fade from recent (strong) to old (pale).
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    charts.push(...[...document.querySelectorAll('canvas[data-by-month-chart]')].map(canvas => {
        const d = JSON.parse(canvas.dataset.byMonthChart);
        const number = new Intl.NumberFormat('en-GB', { minimumFractionDigits: d.decimals, maximumFractionDigits: d.decimals });
        const axisNumber = new Intl.NumberFormat('en-GB', { maximumFractionDigits: d.decimals });
        const earlier = d.years.length - 1;
        const datasets = d.years.map((y, i) => {
            const latest = i === earlier;
            // Blue, from 25% to 90% opaque as the years get more recent.
            const colour = latest ? colours.trend : `rgba(13, 110, 253, ${(0.25 + 0.65 * (earlier > 1 ? i / (earlier - 1) : 1)).toFixed(2)})`;
            return {
                label: String(y.year), data: y.values,
                borderColor: colour, backgroundColor: colour, borderWidth: latest ? 3 : 1.5,
                pointRadius: latest ? 3 : 2, pointHoverRadius: 5, tension: .2, order: latest ? 0 : 1
            };
        });

        return new Chart(canvas, {
            type: 'line',
            data: { labels: months, datasets },
            options: {
                maintainAspectRatio: false,
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    // In year order (the latest is drawn on top, which would otherwise put it first).
                    legend: { labels: { sort: (a, b) => a.datasetIndex - b.datasetIndex } },
                    tooltip: {
                        // Latest year first.
                        itemSort: (a, b) => b.datasetIndex - a.datasetIndex,
                        callbacks: { label: item => `${item.dataset.label}: ${number.format(item.parsed.y)} ${d.unit} a day` }
                    }
                },
                scales: {
                    x: { grid: { display: false } },
                    y: { beginAtZero: true, ticks: { callback: v => axisNumber.format(v) }, title: { display: true, text: `${d.unit} a day` } }
                }
            }
        });
    }));

    // Follow the light/dark theme (color-mode.js).
    document.addEventListener('themechange', () => {
        styleDefaults();
        charts.forEach(chart => chart.update());
    });
})();
