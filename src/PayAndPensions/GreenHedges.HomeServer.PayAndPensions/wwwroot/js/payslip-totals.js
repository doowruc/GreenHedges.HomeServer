// Shows a payslip form's gross, deductions and net as its amounts are typed (_PayslipForm). The saved figures are
// worked out again on the server; this is only a guide while typing.
(() => {
    'use strict';
    const form = document.querySelector('[data-payslip-totals]');
    if (!form) return;

    const money = new Intl.NumberFormat('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const sum = kind => [...form.querySelectorAll(`input[data-kind="${kind}"]`)]
        .reduce((total, input) => total + (Number.parseFloat(input.value) || 0), 0);

    const update = () => {
        const gross = sum('Earning');
        const deductions = sum('Deduction');
        form.querySelector('[data-total="gross"]').textContent = money.format(gross);
        form.querySelector('[data-total="deductions"]').textContent = money.format(deductions);
        form.querySelector('[data-total="net"]').textContent = money.format(gross - deductions);
    };

    form.addEventListener('input', update);
    update();
})();
