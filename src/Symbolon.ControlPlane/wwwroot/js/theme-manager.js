/**
 * ==============================================================================
 * Symbolon Control Plane - Multi-Theme Manager
 * Controls switching between:
 *  - Modern Enterprise (Dark Slate)
 *  - DOS (FoxPro 2.6 TUI)
 *  - Cyberpunk 2077 HUD (Neon Cyan/Pink/Yellow)
 *  - Apple iOS (Human Interface Glassmorphism with Day/Night/System regime)
 * ==============================================================================
 */

(function(window) {
    'use strict';

    const THEME_STORAGE_KEY = 'symbolon_theme';
    const APPLE_REGIME_STORAGE_KEY = 'symbolon_apple_mode';

    const SUPPORTED_THEMES = ['modern', 'retro', 'cyberpunk', 'apple'];
    const SUPPORTED_REGIMES = ['system', 'light', 'dark'];

    function getAppTheme() {
        const stored = localStorage.getItem(THEME_STORAGE_KEY) || localStorage.getItem('symbolon-theme');
        if (stored && SUPPORTED_THEMES.includes(stored)) {
            return stored;
        }
        return 'modern';
    }

    function getAppleRegime() {
        const stored = localStorage.getItem(APPLE_REGIME_STORAGE_KEY);
        if (stored && SUPPORTED_REGIMES.includes(stored)) {
            return stored;
        }
        return 'system';
    }

    function computeEffectiveAppleTheme(regime) {
        if (regime === 'light') return 'light';
        if (regime === 'dark') return 'dark';
        if (window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches) {
            return 'light';
        }
        return 'dark';
    }

    function applyTheme(theme, regime) {
        if (!SUPPORTED_THEMES.includes(theme)) {
            theme = 'modern';
        }
        if (!SUPPORTED_REGIMES.includes(regime)) {
            regime = getAppleRegime();
        }

        const html = document.documentElement;
        html.setAttribute('data-theme', theme);
        html.setAttribute('data-apple-mode', regime);

        const effective = computeEffectiveAppleTheme(regime);
        html.setAttribute('data-apple-effective-theme', effective);

        // Layout synchronization between Retro FoxPro TUI and Modern/Cyber/Apple Web layouts
        const mainWrapper = document.querySelector('.main-wrapper');
        const sidebar = document.querySelector('.sidebar');
        const contentArea = document.querySelector('.content-area');
        const retroWin = document.getElementById('retro-content-win');
        const retroWorkspace = document.querySelector('.retro-workspace');
        const retroTopBar = document.querySelector('.retro-top-bar');
        const retroBottomBar = document.querySelector('.retro-bottom-bar');

        if (theme === 'retro') {
            document.body.classList.add('theme-retro');
            if (retroWorkspace) retroWorkspace.style.display = 'flex';
            if (retroTopBar) retroTopBar.style.display = 'flex';
            if (retroBottomBar) retroBottomBar.style.display = 'block';
            if (mainWrapper) mainWrapper.style.display = 'none';
            if (sidebar) sidebar.style.display = 'none';

            if (contentArea && retroWin && contentArea.parentElement !== retroWin) {
                retroWin.appendChild(contentArea);
                contentArea.classList.add('retro-view-body');
            }
        } else {
            document.body.classList.remove('theme-retro');
            if (retroWorkspace) retroWorkspace.style.display = 'none';
            if (retroTopBar) retroTopBar.style.display = 'none';
            if (retroBottomBar) retroBottomBar.style.display = 'none';
            if (mainWrapper) mainWrapper.style.display = 'flex';
            if (sidebar) sidebar.style.display = 'flex';

            if (contentArea && mainWrapper && contentArea.parentElement !== mainWrapper) {
                mainWrapper.appendChild(contentArea);
                contentArea.classList.remove('retro-view-body');
            }
        }

        updateThemeControls(theme, regime, effective);

        window.dispatchEvent(new CustomEvent('symbolon:themeChanged', {
            detail: { theme, regime, effectiveAppleTheme: effective }
        }));
    }

    function updateThemeControls(theme, regime, effective) {
        // 1. Update Config View Theme Cards
        SUPPORTED_THEMES.forEach(t => {
            const card = document.getElementById(`theme-card-${t}`);
            const tag = document.getElementById(`tag-theme-active-${t}`);
            if (card) {
                card.classList.toggle('border-active', t === theme);
            }
            if (tag) {
                tag.style.display = t === theme ? 'inline-block' : 'none';
            }
        });

        // 2. Update Topbar Theme Switcher buttons
        document.querySelectorAll('.theme-btn-compact').forEach(btn => {
            btn.classList.toggle('active', btn.getAttribute('data-theme') === theme);
        });

        // 3. Update Retro Topbar theme links
        document.querySelectorAll('.retro-theme-link').forEach(link => {
            link.classList.toggle('active', link.getAttribute('data-theme') === theme);
        });

        // 4. Update Modal theme buttons
        document.querySelectorAll('[data-modal-theme]').forEach(btn => {
            const isMatch = btn.getAttribute('data-modal-theme') === theme;
            btn.classList.toggle('btn-primary', isMatch);
            btn.classList.toggle('btn-secondary', !isMatch);
        });

        // 5. Update Apple Regime Segmented Control buttons
        document.querySelectorAll('[data-apple-regime]').forEach(btn => {
            btn.classList.toggle('active', btn.getAttribute('data-apple-regime') === regime);
        });

        // 6. Topbar Apple quick toggle button
        const topbarAppleToggle = document.getElementById('topbar-apple-toggle');
        if (topbarAppleToggle) {
            topbarAppleToggle.style.display = theme === 'apple' ? 'inline-flex' : 'none';
            let label = '🌓 Auto';
            if (regime === 'light') label = '☀️ Day';
            if (regime === 'dark') label = '🌙 Night';
            topbarAppleToggle.innerHTML = label;
        }
    }

    function setAppTheme(theme) {
        if (!SUPPORTED_THEMES.includes(theme)) theme = 'modern';
        localStorage.setItem(THEME_STORAGE_KEY, theme);
        localStorage.setItem('symbolon-theme', theme);
        applyTheme(theme, getAppleRegime());
    }

    function setAppleRegime(regime) {
        if (!SUPPORTED_REGIMES.includes(regime)) regime = 'system';
        localStorage.setItem(APPLE_REGIME_STORAGE_KEY, regime);
        applyTheme(getAppTheme(), regime);
    }

    function cycleAppleRegime() {
        const current = getAppleRegime();
        const next = current === 'system' ? 'light' : (current === 'light' ? 'dark' : 'system');
        setAppleRegime(next);
    }

    function initTheme() {
        const theme = getAppTheme();
        const regime = getAppleRegime();
        applyTheme(theme, regime);

        if (window.matchMedia) {
            window.matchMedia('(prefers-color-scheme: light)').addEventListener('change', () => {
                if (getAppleRegime() === 'system' && getAppTheme() === 'apple') {
                    applyTheme('apple', 'system');
                }
            });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initTheme);
    } else {
        initTheme();
    }

    // Expose global Theme API
    window.ThemeManager = {
        getAppTheme,
        setAppTheme,
        getAppleRegime,
        setAppleRegime,
        cycleAppleRegime,
        applyTheme,
        SUPPORTED_THEMES,
        SUPPORTED_REGIMES
    };

    window.setAppTheme = setAppTheme;
    window.setAppleRegime = setAppleRegime;
    window.cycleAppleRegime = cycleAppleRegime;

})(window);
