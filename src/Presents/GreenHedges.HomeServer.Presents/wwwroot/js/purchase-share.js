// Keeps a purchase form's "Counts as" up to date as it's filled in: price × quantity, halved for each split ticked,
// rounded to the penny as the server does (Services/Shares.cs). Works on every [data-purchase-share] block.
(() => {
    const pounds = new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP' });

    for (const block of document.querySelectorAll('[data-purchase-share]')) {
        const price = block.querySelector('[data-share-price]');
        const quantity = block.querySelector('[data-share-quantity]');
        const splits = [...block.querySelectorAll('[data-share-split]')];
        const result = block.querySelector('[data-share-result]');

        const update = () => {
            const p = parseFloat(price.value);
            const q = Math.max(parseInt(quantity.value, 10) || 1, 1);
            if (!(p > 0)) { result.textContent = '–'; return; }
            const divisor = splits.reduce((d, box) => box.checked ? d * 2 : d, 1);
            // In whole pence, so half a penny rounds up as on the server: floor((2n + d) / 2d) is n ÷ d rounded half up.
            const pence = Math.round(p * 100) * q;
            result.textContent = pounds.format(Math.floor((2 * pence + divisor) / (2 * divisor)) / 100);
        };

        for (const input of [price, quantity, ...splits]) input.addEventListener('input', update);
        update();
    }
})();
