// Light / dark / system colour mode using Bootstrap 5.3's data-bs-theme.
// Loaded synchronously in <head> so the theme is set before the page paints (no flash).
// The choice is stored per browser in localStorage; "system" (the default) follows the OS.
(() => {
    'use strict';

    const storageKey = 'theme';
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const icons = { light: 'bi-sun-fill', dark: 'bi-moon-stars-fill', auto: 'bi-circle-half' };
    const names = { light: 'Light', dark: 'Dark', auto: 'System' };

    const getPreference = () => {
        try {
            const stored = localStorage.getItem(storageKey);
            return stored === 'light' || stored === 'dark' ? stored : 'auto';
        } catch {
            return 'auto';
        }
    };

    const setPreference = value => {
        try {
            if (value === 'auto') localStorage.removeItem(storageKey);
            else localStorage.setItem(storageKey, value);
        } catch { /* storage unavailable – the choice just won't persist */ }
    };

    const resolve = preference => preference === 'auto' ? (media.matches ? 'dark' : 'light') : preference;

    const applyTheme = () => {
        const theme = resolve(getPreference());
        const root = document.documentElement;
        if (root.getAttribute('data-bs-theme') === theme) return;
        root.setAttribute('data-bs-theme', theme);
        // Lets scripts that draw their own colours (e.g. charts) restyle themselves.
        document.dispatchEvent(new CustomEvent('themechange', { detail: { theme } }));
    };

    const showActive = () => {
        const preference = getPreference();
        // The user menu's Theme item shows the current choice.
        const icon = document.getElementById('theme-icon');
        if (icon) icon.className = `bi ${icons[preference]} me-2`;
        const name = document.getElementById('theme-name');
        if (name) name.textContent = names[preference];

        document.querySelectorAll('[data-theme-value]').forEach(button => {
            const active = button.dataset.themeValue === preference;
            button.classList.toggle('active', active);
            button.setAttribute('aria-pressed', active);
        });
    };

    applyTheme();

    // Follow OS changes while on "system".
    media.addEventListener('change', () => {
        if (getPreference() === 'auto') applyTheme();
    });

    // Keep other open tabs in sync.
    window.addEventListener('storage', e => {
        if (e.key === storageKey || e.key === null) {
            applyTheme();
            showActive();
        }
    });

    document.addEventListener('DOMContentLoaded', () => {
        showActive();
        document.querySelectorAll('[data-theme-value]').forEach(button =>
            button.addEventListener('click', () => {
                setPreference(button.dataset.themeValue);
                applyTheme();
                showActive();
                // Close the menu it's in, as choosing any other item would (it stays open for clicks inside).
                const toggle = button.closest('.dropdown')?.querySelector('[data-bs-toggle="dropdown"]');
                if (toggle && window.bootstrap) bootstrap.Dropdown.getOrCreateInstance(toggle).hide();
            }));

        // Closing the menu folds its Theme options away again (at once, as the menu has gone), so it reopens tidy.
        const options = document.getElementById('theme-options');
        options?.closest('.dropdown')?.addEventListener('hidden.bs.dropdown', () => {
            options.classList.remove('show');
            document.querySelectorAll('[data-bs-target="#theme-options"]').forEach(button => {
                button.classList.add('collapsed');
                button.setAttribute('aria-expanded', 'false');
            });
        });
    });

    // Current Chart.js-friendly colours, read from Bootstrap's CSS variables.
    window.themeColors = () => {
        const style = getComputedStyle(document.documentElement);
        return {
            text: style.getPropertyValue('--bs-secondary-color').trim(),
            grid: style.getPropertyValue('--bs-border-color-translucent').trim()
        };
    };
})();
