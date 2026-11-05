// A right-click menu for table rows: rows marked data-row-menu, with data-edit and data-delete URLs, open #row-menu
// (a Bootstrap .dropdown-menu) at the pointer, on a right-click or, on a touch screen, a press and hold. Clicking
// elsewhere, scrolling or Escape closes it. Delete goes to its confirmation page, so the menu only links.
(() => {
    'use strict';
    const menu = document.getElementById('row-menu');
    if (!menu) return;
    const links = { edit: menu.querySelector('[data-action="edit"]'), delete: menu.querySelector('[data-action="delete"]') };

    const close = () => menu.classList.remove('show');

    const open = (row, x, y) => {
        links.edit.href = row.dataset.edit;
        links.delete.href = row.dataset.delete;
        Object.assign(menu.style, { position: 'fixed', inset: 'auto', left: '0', top: '0' });
        menu.classList.add('show');
        // Keep it on screen: flip left or up where it would run off the edge.
        const { width, height } = menu.getBoundingClientRect();
        menu.style.left = `${Math.max(0, x + width > window.innerWidth ? x - width : x)}px`;
        menu.style.top = `${Math.max(0, y + height > window.innerHeight ? y - height : y)}px`;
        links.edit.focus({ preventScroll: true });
    };

    document.addEventListener('contextmenu', e => {
        const row = e.target.closest('tr[data-row-menu]');
        if (!row || e.target.closest('a')) { close(); return; }   // a link keeps the browser's own menu
        e.preventDefault();
        open(row, e.clientX, e.clientY);
    });

    // Press and hold, for touch screens that don't send contextmenu (iOS Safari).
    let hold = null;
    document.addEventListener('touchstart', e => {
        const row = e.target.closest('tr[data-row-menu]');
        if (!row || e.target.closest('a') || e.touches.length !== 1) return;
        const { clientX, clientY } = e.touches[0];
        hold = setTimeout(() => { hold = 'opened'; open(row, clientX, clientY); }, 500);
    }, { passive: true });
    const cancelHold = () => { if (typeof hold === 'number') clearTimeout(hold); };
    document.addEventListener('touchmove', () => { cancelHold(); hold = null; }, { passive: true });
    document.addEventListener('touchend', e => {
        if (hold === 'opened') e.preventDefault();   // don't let the lifted finger click what's under it
        cancelHold();
        hold = null;
    });

    document.addEventListener('click', e => { if (!menu.contains(e.target)) close(); });
    document.addEventListener('keydown', e => { if (e.key === 'Escape') close(); });
    window.addEventListener('scroll', close, true);
    window.addEventListener('resize', close);
})();
