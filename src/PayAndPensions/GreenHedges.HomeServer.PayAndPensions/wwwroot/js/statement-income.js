// The statement form's projected monthly income (_StatementForm): typing it fills in the annual income (× 12), and
// typing the annual fills it in (÷ 12). Only the annual is posted and stored: the monthly input has no name.
(() => {
    'use strict';
    const monthly = document.querySelector('[data-monthly-income]');
    const annual = document.querySelector('[data-annual-income]');
    if (!monthly || !annual) return;

    // Blank (or not a number) gives blank; else to the penny.
    const fill = (from, to, convert) => {
        const amount = Number.parseFloat(from.value);
        to.value = from.value.trim() === '' || Number.isNaN(amount) ? '' : (Math.round(convert(amount) * 100) / 100).toFixed(2);
    };

    monthly.addEventListener('input', () => fill(monthly, annual, amount => amount * 12));
    annual.addEventListener('input', () => fill(annual, monthly, amount => amount / 12));
    fill(annual, monthly, amount => amount / 12);
})();
