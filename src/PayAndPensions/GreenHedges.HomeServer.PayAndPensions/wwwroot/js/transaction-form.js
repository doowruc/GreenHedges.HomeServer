// The form for a transaction entered by hand (Transactions/Manual): shows the fund, units and price only for a
// purchase or sale, and works a deal's value out from its units and price, or its price from its value. A guide while
// typing: the server works both out again.
(() => {
    'use strict';
    const form = document.querySelector('[data-transaction-form]');
    if (!form) return;
    const kind = form.querySelector('[data-kind]');
    const units = form.querySelector('[data-units]');
    const price = form.querySelector('[data-price]');
    const value = form.querySelector('[data-value]');

    const isDeal = () => kind.selectedOptions[0]?.dataset.deal === 'true';
    const showDealFields = () => form.querySelectorAll('[data-deal-only]').forEach(field => { field.hidden = !isDeal(); });
    const number = input => (input.value.trim() === '' ? NaN : Number.parseFloat(input.value));

    const fillValue = () => {
        const [u, p] = [number(units), number(price)];
        if (isDeal() && u > 0 && p > 0) value.value = (Math.round(u * p) / 100).toFixed(2);
    };
    const fillPrice = () => {
        const [u, v] = [number(units), number(value)];
        if (isDeal() && u > 0 && v > 0) price.value = String(Number((v * 100 / u).toFixed(4)));
    };

    kind.addEventListener('change', showDealFields);
    units.addEventListener('input', () => (number(price) > 0 ? fillValue() : fillPrice()));
    price.addEventListener('input', fillValue);
    value.addEventListener('input', fillPrice);
    showDealFields();
})();
