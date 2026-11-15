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
    // units a day in each month, the years across and a line per month, to see how each month's use changes over the
    // years (how January is trending, say). A month's line joins the years it has a figure for, skipping those
    // without; the points are the figures. Click a month in the legend to hide or show it.
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    // Round the colour wheel with the year: blues in winter, through reds and yellows in summer, to greens in autumn.
    const monthColour = month => `hsl(${(240 + month * 30) % 360}, 70%, 50%)`;
    charts.push(...[...document.querySelectorAll('canvas[data-by-month-chart]')].map(canvas => {
        const d = JSON.parse(canvas.dataset.byMonthChart);
        const number = new Intl.NumberFormat('en-GB', { minimumFractionDigits: d.decimals, maximumFractionDigits: d.decimals });
        const axisNumber = new Intl.NumberFormat('en-GB', { maximumFractionDigits: d.decimals });
        // Every year from the first to the last, so the years are evenly spaced even if one has no figures at all.
        const byYear = new Map(d.years.map(y => [y.year, y.values]));
        const years = [];
        for (let year = d.years[0].year; year <= d.years[d.years.length - 1].year; year++) years.push(year);
        const datasets = months.map((label, month) => ({
            label, data: years.map(year => byYear.get(year)?.[month] ?? null),
            borderColor: monthColour(month), backgroundColor: monthColour(month), borderWidth: 2,
            pointRadius: 3, pointHoverRadius: 5, tension: 0, spanGaps: true
        }));

        return new Chart(canvas, {
            type: 'line',
            data: { labels: years.map(String), datasets },
            options: {
                maintainAspectRatio: false,
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    tooltip: {
                        // Highest first, as the lines are stacked on the chart.
                        itemSort: (a, b) => b.parsed.y - a.parsed.y,
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
