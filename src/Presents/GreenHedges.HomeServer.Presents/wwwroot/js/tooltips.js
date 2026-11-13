// Every element with a title gets a Bootstrap tooltip instead of the browser's own, so pages only write title="…".
// Loaded by the layout after Bootstrap. Tooltips go on <body>, so a scrolling table (.table-responsive) can't clip
// them. Elements Bootstrap already drives (e.g. the account menu's dropdown button) keep their title as it is.
(() => {
    'use strict';
    document.querySelectorAll('[title]:not([data-bs-toggle])').forEach(el =>
        new bootstrap.Tooltip(el, { container: 'body' }));
})();
