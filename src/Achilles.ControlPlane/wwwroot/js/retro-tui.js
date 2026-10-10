/**
 * ==============================================================================
 * Symbolon Retro FoxPro / DOS TUI Keyboard Engine & UI Controller
 * Kompletná podpora navigácie z klávesnice (šípky, Enter, Esc, hotkeys, F1-F10)
 * ==============================================================================
 */

(function () {
    'use strict';

    // Stav klávesnicového manažéra
    const state = {
        activeContext: 'menu', // 'menu', 'submenu', 'content', 'modal'
        mainMenuIndex: 0,
        submenuIndex: 0,
        contentRowIndex: 0,
        isSubmenuOpen: false,
        activeModalId: null
    };

    // Položky hlavného kaskádového menu (presne podľa FoxPro rozvrhnutia)
    const mainMenuItems = [
        { id: 'vstupy', label: 'Vstupy (Prehľad)', hotkey: 'V', view: 'view-overview', hasSub: false },
        { id: 'licencie', label: 'Licencie', hotkey: 'L', view: 'view-licenses', hasSub: false },
        { id: 'kluce', label: 'Kľúče & PQC', hotkey: 'K', view: 'view-keys', hasSub: false },
        { id: 'ciselniky', label: 'Číselníky', hotkey: 'Č', view: null, hasSub: true },
        { isSeparator: true },
        { id: 'relaye', label: 'Relay uzly', hotkey: 'R', view: 'view-relays', hasSub: false },
        { id: 'mesh', label: 'Relay Mesh & Klastre', hotkey: 'M', view: 'view-mesh', hasSub: false },
        { id: 'audit', label: 'Auditný denník', hotkey: 'A', view: 'view-audit', hasSub: false },
        { id: 'reports', label: 'Reporty & True-Up', hotkey: 'y', view: 'view-reports', hasSub: false },
        { id: 'airgap', label: 'Air-Gap portál', hotkey: 'G', view: 'view-airgap', hasSub: false },
        { id: 'wasm', label: 'Wasm Validátor', hotkey: 'D', view: 'view-wasm', hasSub: false },
        { id: 'tokens', label: 'Tokeny & Kredity', hotkey: 'E', view: 'view-tokens', hasSub: false },
        { id: 'migrate', label: 'Migrácia (FlexNet)', hotkey: 'I', view: 'view-migrate', hasSub: false },
        { id: 'features', label: 'F[u]nkcie & Moduly', hotkey: 'U', view: 'view-features', hasSub: false },
        { id: 'revocations', label: 'Revokácie & CRL', hotkey: 'X', view: 'view-revocations', hasSub: false },
        { id: 'queue', label: 'Licenčný Rad (FLT-31)', hotkey: 'Q', view: 'view-queue', hasSub: false },
        { id: 'policyrules', label: 'Pravidlá & Options', hotkey: 'Z', view: 'view-policy-rules', hasSub: false },
        { id: 'machines', label: 'Stroje & Node-Lock', hotkey: 'N', view: 'view-machines', hasSub: false },
        { id: 'webhooky', label: 'Webhooky', hotkey: 'W', view: 'view-webhooks', hasSub: false },
        { id: 'experiments', label: 'A/B Experimenty (AB-1)', hotkey: 'B', view: 'view-experiments', hasSub: false },
        { id: 'pqc', label: 'Post-Quantum Era (M7)', hotkey: 'q', view: 'view-pqc', hasSub: false },
        { isSeparator: true },
        { id: 'sulad', label: 'Súlad & CRA / SBOM', hotkey: 'S', view: 'view-system', hasSub: false },
        { id: 'apikeys', label: 'API Kľúče & Merkle', hotkey: 'T', view: 'view-apikeys', hasSub: false },
        { id: 'konfiguracia', label: 'Konfigurácia', hotkey: 'O', view: 'view-config', hasSub: false },
        { id: 'pomoc', label: 'Príručka & Pomoc (F1)', hotkey: 'P', view: 'view-help', hasSub: false }
    ];

    // Položky kaskádového podmenu "Číselníky" (ako na screenshotu)
    const submenuItems = [
        { id: 'sub-tenants', label: 'Adresár zákazníkov', hotkey: 'A', action: () => alert('Adresár zákazníkov ISV') },
        { id: 'sub-products', label: 'Katalóg produktov', hotkey: 'K', action: () => alert('Katalóg chránených aplikácií') },
        { id: 'sub-policies', label: 'Licenčné politiky', hotkey: 'P', action: () => alert('Šablóny licenčných politík') },
        { id: 'sub-rules', label: 'Options & Pravidlá (FLT-24)', hotkey: 'O', action: () => { switchRetroView('view-policy-rules'); if (typeof window.loadPolicyRulesView === 'function') window.loadPolicyRulesView(); } },
        { id: 'sub-features', label: 'Katalóg modulov a suít', hotkey: 'F', action: () => { switchRetroView('view-features'); if (typeof loadFeaturesView === 'function') loadFeaturesView(); } },
        { id: 'sub-jwks', label: 'Verejné kľúče (JWKS)', hotkey: 'V', action: () => { if (typeof openJwksModal === 'function') openJwksModal(); } },
        { id: 'sub-kms', label: 'Cloud KMS & HSM Stav', hotkey: 'M', action: () => { switchRetroView('view-keys'); if (typeof loadKmsHierarchy === 'function') loadKmsHierarchy(); } },
        { id: 'sub-hierarchy', label: '3-Úrovňová Hierarchia (§9.3)', hotkey: '3', action: () => { switchRetroView('view-keys'); if (typeof verifyKeyHierarchy === 'function') verifyKeyHierarchy(); } },
        { id: 'sub-rates', label: 'Sadzby & Kredity (Tokeny)', hotkey: 'S', action: () => { switchRetroView('view-tokens'); if (typeof loadTokenRates === 'function') loadTokenRates(); } },
        { id: 'sub-borrow', label: 'Offline Roaming (.symlease)', hotkey: 'B', action: () => { switchRetroView('view-overview'); if (typeof openBorrowModal === 'function') openBorrowModal(); } },
        { id: 'sub-nodes', label: 'Hardvérové odtlačky & Stroje', hotkey: 'H', action: () => { switchRetroView('view-machines'); if (typeof window.loadMachinesView === 'function') window.loadMachinesView(); } }
    ];

    // Inicializácia po načítaní DOM
    document.addEventListener('DOMContentLoaded', () => {
        initRetroDOM();
        bindKeyboardListeners();
        updateMenuHighlights();
    });

    /**
     * Zostaví HTML štruktúru pre retro FoxPro lišty a kaskádové menu
     */
    function initRetroDOM() {
        // 1. Horná lišta
        const topBar = document.createElement('header');
        topBar.className = 'retro-top-bar';
        topBar.innerHTML = `
            <ul class="retro-menu-bar">
                <li class="retro-menu-item" onclick="toggleMainMenu()" tabindex="0">≡ <span class="hotkey">A</span>chilles</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-overview')" tabindex="0"><span class="hotkey">P</span>rehľad</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-licenses')" tabindex="0"><span class="hotkey">L</span>icencie</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-keys')" tabindex="0"><span class="hotkey">K</span>ľúče</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-relays')" tabindex="0"><span class="hotkey">R</span>elaye</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-mesh')" tabindex="0"><span class="hotkey">M</span>esh</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-audit')" tabindex="0"><span class="hotkey">A</span>udit</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-reports')" tabindex="0">Report<span class="hotkey">y</span></li>
                <li class="retro-menu-item" onclick="switchRetroView('view-airgap')" tabindex="0">Air-<span class="hotkey">G</span>ap</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-wasm')" tabindex="0"><span class="hotkey">W</span>asm</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-tokens')" tabindex="0">Tok<span class="hotkey">e</span>ny</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-migrate')" tabindex="0">M<span class="hotkey">i</span>grácia</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-webhooks')" tabindex="0"><span class="hotkey">W</span>ebhooky</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-revocations')" tabindex="0">Revo<span class="hotkey">k</span>ácie</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-queue')" tabindex="0"><span class="hotkey">Q</span>ueue Rad</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-policy-rules')" tabindex="0"><span class="hotkey">Z</span>ásady (FLT-24)</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-machines')" tabindex="0"><span class="hotkey">N</span>ode-Lock</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-experiments')" tabindex="0">A/<span class="hotkey">B</span> Exp</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-pqc')" tabindex="0">P<span class="hotkey">Q</span>C M7</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-system')" tabindex="0"><span class="hotkey">S</span>úlad CRA</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-config')" tabindex="0">K<span class="hotkey">o</span>nfigurácia</li>
                <li class="retro-menu-item" onclick="switchRetroView('view-help')" tabindex="0"><span class="hotkey">H</span>elp (F1)</li>
            </ul>
            <div style="font-size: 11px; color: #444; font-weight: bold; display: flex; align-items: center; gap: 8px;">
                <span class="retro-theme-switcher">[ Téma: <a href="javascript:void(0)" class="retro-theme-link active" data-theme="retro" onclick="setAppTheme('retro')"><span class="hotkey">D</span>OS</a> | <a href="javascript:void(0)" class="retro-theme-link" data-theme="modern" onclick="setAppTheme('modern')"><span class="hotkey">M</span>odern</a> | <a href="javascript:void(0)" class="retro-theme-link" data-theme="cyberpunk" onclick="setAppTheme('cyberpunk')"><span class="hotkey">C</span>yber</a> | <a href="javascript:void(0)" class="retro-theme-link" data-theme="apple" onclick="setAppTheme('apple')"><span class="hotkey">A</span>pple</a> ]</span>
                <span class="retro-lang-switcher">[ <a href="javascript:void(0)" class="retro-lang-link active" data-lang="sk" onclick="setAppLanguage('sk')"><span class="hotkey">S</span>K</a> | <a href="javascript:void(0)" class="retro-lang-link" data-lang="en" onclick="setAppLanguage('en')"><span class="hotkey">E</span>N</a> | <a href="javascript:void(0)" class="retro-lang-link" data-lang="de" onclick="setAppLanguage('de')"><span class="hotkey">D</span>E</a> ]</span>
                <span id="retro-server-clock">--:--:--</span> | <span id="retro-conn-status" style="color: green;">● ONLINE</span>
            </div>
        `;
        const isRetroActive = (window.ThemeManager && window.ThemeManager.getAppTheme() === 'retro') || document.documentElement.getAttribute('data-theme') === 'retro';
        if (!isRetroActive) {
            topBar.style.display = 'none';
        }
        document.body.insertBefore(topBar, document.body.firstChild);

        // 2. Hlavný retro workspace obalujúci obsah
        const mainWrapper = document.querySelector('.main-wrapper');
        if (mainWrapper) {
            if (isRetroActive) {
                mainWrapper.style.display = 'none';
            }
            const workspace = document.createElement('div');
            workspace.className = 'retro-workspace';
            if (!isRetroActive) {
                workspace.style.display = 'none';
            }

            // Ľavé okno menu
            const leftMenuWindow = document.createElement('aside');
            leftMenuWindow.className = 'retro-window-menu';
            leftMenuWindow.id = 'retro-left-menu';

            let menuListHtml = '<div class="retro-window-header">HLAVNÉ MENU (F10)</div><ul class="retro-menu-list" id="retro-main-list">';
            mainMenuItems.forEach((item, idx) => {
                if (item.isSeparator) {
                    menuListHtml += '<li class="retro-separator"></li>';
                } else {
                    const hotkeyLetter = item.hotkey;
                    const labelFormatted = item.label.replace(hotkeyLetter, `<span class="hotkey">${hotkeyLetter}</span>`);
                    const arrow = item.hasSub ? '►' : '';
                    menuListHtml += `
                        <li class="retro-list-item" data-index="${idx}" onclick="handleMainMenuItemClick(${idx})">
                            <span>${labelFormatted}</span>
                            <span>${arrow}</span>
                        </li>
                    `;
                }
            });
            menuListHtml += '</ul>';
            leftMenuWindow.innerHTML = menuListHtml;

            // Kaskádové podmenu (Navy popup)
            const popupSubmenu = document.createElement('div');
            popupSubmenu.className = 'retro-cascading-popup';
            popupSubmenu.id = 'retro-popup-sub';
            let subListHtml = '';
            submenuItems.forEach((sub, sIdx) => {
                const hotkeyLetter = sub.hotkey;
                const labelFormatted = sub.label.replace(hotkeyLetter, `<span class="hotkey">${hotkeyLetter}</span>`);
                subListHtml += `
                    <div class="retro-popup-item" data-subindex="${sIdx}" onclick="handleSubmenuItemClick(${sIdx})">
                        ${labelFormatted}
                    </div>
                `;
            });
            popupSubmenu.innerHTML = subListHtml;
            leftMenuWindow.appendChild(popupSubmenu);

            // Presun existujúceho obsahu do retro-content-window
            const contentWindow = document.createElement('main');
            contentWindow.className = 'retro-content-window';
            contentWindow.id = 'retro-content-win';

            const titleBar = document.createElement('div');
            titleBar.className = 'retro-content-titlebar';
            titleBar.innerHTML = `
                <span id="retro-window-title">PREHĽAD LICENČNÉHO SERVERA</span>
                <div>
                    <button class="retro-btn retro-btn-primary" onclick="openModal('modal-issue-license')">+ Vystaviť [V]</button>
                    <button class="retro-btn" onclick="switchRetroView('view-help')">Príručka [F1]</button>
                </div>
            `;
            contentWindow.appendChild(titleBar);

            const contentArea = document.querySelector('.content-area');
            if (contentArea && isRetroActive) {
                contentArea.classList.add('retro-view-body');
                contentWindow.appendChild(contentArea);
            }

            workspace.appendChild(leftMenuWindow);
            workspace.appendChild(contentWindow);

            document.body.appendChild(workspace);
        }

        // 3. Spodná dvojriadková lišta: F1-F10 a živá info lišta systémových zdrojov
        const bottomBar = document.createElement('footer');
        bottomBar.className = 'retro-bottom-bar';
        if (!isRetroActive) {
            bottomBar.style.display = 'none';
        }
        bottomBar.innerHTML = `
            <div class="retro-fkey-row">
                <button class="fkey-btn" onclick="switchRetroView('view-help')"><span class="fkey-badge">F1</span> Príručka</button>
                <button class="fkey-btn" onclick="switchRetroView('view-overview')"><span class="fkey-badge">F2</span> Prehľad</button>
                <button class="fkey-btn" onclick="switchRetroView('view-licenses')"><span class="fkey-badge">F3</span> Licencie</button>
                <button class="fkey-btn" onclick="switchRetroView('view-keys')"><span class="fkey-badge">F4</span> Kľúče</button>
                <button class="fkey-btn" onclick="switchRetroView('view-relays')"><span class="fkey-badge">F5</span> Relaye</button>
                <button class="fkey-btn" onclick="switchRetroView('view-audit')"><span class="fkey-badge">F6</span> Audit</button>
                <button class="fkey-btn" onclick="switchRetroView('view-airgap')"><span class="fkey-badge">F7</span> AirGap</button>
                <button class="fkey-btn" onclick="switchRetroView('view-webhooks')"><span class="fkey-badge">F8</span> Webhook</button>
                <button class="fkey-btn" onclick="switchRetroView('view-system')"><span class="fkey-badge">F9</span> Súlad</button>
                <button class="fkey-btn" onclick="switchRetroView('view-revocations')"><span class="fkey-badge">F11</span> Revokácie</button>
                <button class="fkey-btn" onclick="switchRetroView('view-reports')"><span class="fkey-badge">F12</span> Reporty</button>
                <button class="fkey-btn" onclick="toggleMainMenu()"><span class="fkey-badge">F10</span> Menu</button>
                <button class="fkey-btn" onclick="handleEscKey()"><span class="fkey-badge">ESC</span> Späť</button>
            </div>
            <div class="retro-telemetry-row">
                <div class="retro-telemetry-segment">
                    <span class="retro-telemetry-label">CPU:</span>
                    <span class="retro-telemetry-val" id="telemetry-cpu">0.0%</span>
                </div>
                <div class="retro-telemetry-segment">
                    <span class="retro-telemetry-label">RAM:</span>
                    <span class="retro-telemetry-val" id="telemetry-ram">0.0 MB</span>
                </div>
                <div class="retro-telemetry-segment">
                    <span class="retro-telemetry-label">DISK:</span>
                    <span class="retro-telemetry-val" id="telemetry-disk">0.0 MB</span>
                </div>
                <div class="retro-telemetry-segment">
                    <span class="retro-telemetry-label">INTERNET:</span>
                    <span class="retro-telemetry-val" id="telemetry-net">0.0 KB/s</span>
                </div>
                <div class="retro-telemetry-segment">
                    <span class="retro-telemetry-label">IP:</span>
                    <span class="retro-telemetry-val" id="telemetry-ip">127.0.0.1:8080</span>
                </div>
                <div class="retro-telemetry-segment">
                    <span class="retro-telemetry-label">POUŽÍVATEĽ:</span>
                    <span class="retro-telemetry-val" id="telemetry-user" style="color: #00ffff;">admin:super</span>
                </div>
                <div class="retro-telemetry-segment" style="border-right: none; margin-left: auto;">
                    <span class="retro-telemetry-label">ČAS:</span>
                    <span class="retro-telemetry-val" id="retro-server-clock" style="color: #ffffff;">--:--:--</span>
                </div>
            </div>
        `;
        document.body.appendChild(bottomBar);

        // 4. Dialógové okno Pomocníka klávesových skratiek (F1)
        createHelpModal();

        // 5. Spustenie zberu živých systémových metrík z /v1/system/telemetry
        initTelemetryPolling();
    }

    /**
     * Vytvorí retro nápovedu s prehľadom všetkých klávesových skratiek
     */
    function createHelpModal() {
        const modal = document.createElement('div');
        modal.className = 'retro-modal-overlay modal-overlay';
        modal.id = 'modal-retro-help';
        modal.style.display = 'none';
        modal.innerHTML = `
            <div class="retro-modal-box">
                <div class="retro-modal-titlebar">
                    <span>NÁPOVEDA KLÁVESOVÉHO OVLÁDANIA (F1)</span>
                    <button class="retro-btn" onclick="closeHelpModal()">✕</button>
                </div>
                <div class="retro-modal-content" style="font-size: 13px; line-height: 1.6;">
                    <p style="margin-bottom: 8px;"><strong>Systém je 100% ovládateľný bez použitia myši:</strong></p>
                    <table class="retro-table" style="margin-bottom: 12px;">
                        <tr><th style="width: 35%;">Kláves</th><th>Funkcia</th></tr>
                        <tr><td><code>↑ / ↓</code></td><td>Pohyb v menu, podmenu a riadkoch tabuliek</td></tr>
                        <tr><td><code>→ / Enter</code></td><td>Otvorenie kaskádového podmenu / potvrdenie výberu</td></tr>
                        <tr><td><code>← / Escape</code></td><td>Návrat z podmenu / zatvorenie okna / zrušenie</td></tr>
                        <tr><td><code>F1 – F9</code></td><td>Priamy skok na obrazovky (F2 Prehľad, F3 Licencie...)</td></tr>
                        <tr><td><code>F10 / Alt</code></td><td>Aktivácia a fokus hlavného kaskádového menu</td></tr>
                        <tr><td><code>V</code></td><td>Okamžité otvorenie dialógu pre vystavenie licencie</td></tr>
                        <tr><td><code>D</code></td><td>Priame stiahnutie CycloneDX SBOM (JSON)</td></tr>
                        <tr><td><code>O / C</code></td><td>Nastavenie administrátorského API kľúča</td></tr>
                        <tr><td><code>Tab / Shift+Tab</code></td><td>Prepínanie prvkov vo formulároch</td></tr>
                    </table>
                    <p style="font-size: 12px; color: #555;">Žlté a jantárové podčiarknuté písmená sú akcelerátory – ich stlačením priamo aktivujete danú voľbu.</p>
                </div>
                <div class="retro-modal-footer">
                    <button class="retro-btn retro-btn-primary" onclick="closeHelpModal()">[ Pokračovať (Enter) ]</button>
                </div>
            </div>
        `;
        modal.addEventListener('click', (e) => {
            if (e.target === modal) {
                closeHelpModal();
            }
        });
        document.body.appendChild(modal);
    }

    /**
     * Globálny odchytávač klávesnice pre 100% keyboard control
     */
    function bindKeyboardListeners() {
        window.addEventListener('keydown', (e) => {
            // Ak používateľ píše do inputu alebo textarea, neblokujeme bežné písanie,
            // okrem Escape a Enter
            const isInputFocused = ['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement?.tagName);

            if (isInputFocused) {
                if (e.key === 'Escape') {
                    handleEscKey();
                    e.preventDefault();
                } else if (e.key === 'Enter' && document.activeElement?.tagName !== 'TEXTAREA') {
                    // Odoslanie formulára
                }
                return;
            }

            // Funkčné klávesy F1 až F10
            switch (e.key) {
                case 'F1':
                    e.preventDefault();
                    switchRetroView('view-help');
                    return;
                case 'F2':
                    e.preventDefault();
                    switchRetroView('view-overview');
                    return;
                case 'F3':
                    e.preventDefault();
                    switchRetroView('view-licenses');
                    return;
                case 'F4':
                    e.preventDefault();
                    switchRetroView('view-keys');
                    return;
                case 'F5':
                    e.preventDefault();
                    switchRetroView('view-relays');
                    return;
                case 'F6':
                    e.preventDefault();
                    switchRetroView('view-audit');
                    return;
                case 'F7':
                    e.preventDefault();
                    switchRetroView('view-airgap');
                    return;
                case 'F8':
                    e.preventDefault();
                    switchRetroView('view-webhooks');
                    return;
                case 'F9':
                    e.preventDefault();
                    switchRetroView('view-system');
                    return;
                case 'F11':
                    e.preventDefault();
                    switchRetroView('view-revocations');
                    return;
                case 'F12':
                    e.preventDefault();
                    switchRetroView('view-reports');
                    return;
                case 'F10':
                case 'Alt':
                    e.preventDefault();
                    toggleMainMenu();
                    return;
                case 'Escape':
                    e.preventDefault();
                    handleEscKey();
                    return;
            }

            // Šípka hore / dole
            if (e.key === 'ArrowDown') {
                e.preventDefault();
                moveCursor(1);
                return;
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                moveCursor(-1);
                return;
            }

            // Šípka vpravo (vstup do podmenu)
            if (e.key === 'ArrowRight') {
                e.preventDefault();
                if (state.activeContext === 'menu') {
                    const currentItem = mainMenuItems[state.mainMenuIndex];
                    if (currentItem?.hasSub) {
                        openSubmenu();
                    }
                }
                return;
            }

            // Šípka vľavo (návrat z podmenu)
            if (e.key === 'ArrowLeft') {
                e.preventDefault();
                if (state.activeContext === 'submenu') {
                    closeSubmenu();
                }
                return;
            }

            // Enter (potvrdenie / spustenie)
            if (e.key === 'Enter') {
                const helpModal = document.getElementById('modal-retro-help');
                if (helpModal && (helpModal.classList.contains('active') || helpModal.style.display === 'flex')) {
                    e.preventDefault();
                    window.closeHelpModal();
                    return;
                }
                e.preventDefault();
                executeCurrentSelection();
                return;
            }

            // Akcelerátory (priame horúce klávesy)
            const keyUpper = e.key.toUpperCase();

            // Globálne skratky
            if (keyUpper === 'V') {
                e.preventDefault();
                openModal('modal-issue-license');
                return;
            } else if (keyUpper === 'D') {
                e.preventDefault();
                downloadSbomDirect();
                return;
            } else if (keyUpper === 'O' || keyUpper === 'C') {
                e.preventDefault();
                openModal('modal-auth-config');
                return;
            } else if (keyUpper === 'P') {
                e.preventDefault();
                switchRetroView('view-overview');
                return;
            } else if (keyUpper === 'L') {
                e.preventDefault();
                switchRetroView('view-licenses');
                return;
            } else if (keyUpper === 'K') {
                e.preventDefault();
                switchRetroView('view-keys');
                return;
            } else if (keyUpper === 'R') {
                e.preventDefault();
                switchRetroView('view-relays');
                return;
            } else if (keyUpper === 'A') {
                e.preventDefault();
                switchRetroView('view-audit');
                return;
            } else if (keyUpper === 'G') {
                e.preventDefault();
                switchRetroView('view-airgap');
                return;
            } else if (keyUpper === 'W') {
                e.preventDefault();
                switchRetroView('view-webhooks');
                return;
            } else if (keyUpper === 'M') {
                e.preventDefault();
                switchRetroView('view-mesh');
                return;
            } else if (keyUpper === 'T') {
                e.preventDefault();
                switchRetroView('view-apikeys');
                return;
            } else if (keyUpper === 'S') {
                e.preventDefault();
                switchRetroView('view-system');
                return;
            }
        });
    }

    /**
     * Pohyb kurzora v menu alebo podmenu
     */
    function moveCursor(direction) {
        if (state.activeContext === 'submenu') {
            state.submenuIndex = (state.submenuIndex + direction + submenuItems.length) % submenuItems.length;
            updateSubmenuHighlights();
        } else {
            // Pohyb v hlavnom menu (preskakujeme oddeľovače)
            let nextIndex = state.mainMenuIndex;
            do {
                nextIndex = (nextIndex + direction + mainMenuItems.length) % mainMenuItems.length;
            } while (mainMenuItems[nextIndex]?.isSeparator);

            state.mainMenuIndex = nextIndex;
            updateMenuHighlights();
        }
    }

    /**
     * Spustenie aktuálne vybratej položky klávesom Enter
     */
    function executeCurrentSelection() {
        if (state.activeContext === 'submenu') {
            const item = submenuItems[state.submenuIndex];
            if (item && typeof item.action === 'function') {
                item.action();
                closeSubmenu();
            }
        } else {
            const item = mainMenuItems[state.mainMenuIndex];
            if (item) {
                if (item.hasSub) {
                    openSubmenu();
                } else if (item.view) {
                    switchRetroView(item.view);
                } else if (item.action === 'open-config') {
                    switchRetroView('view-config');
                    if (typeof updateConfigApiKeyStatus === 'function') updateConfigApiKeyStatus();
                } else if (item.action === 'open-help') {
                    openHelpModal();
                }
            }
        }
    }

    /**
     * Otvorenie kaskádového podmenu
     */
    function openSubmenu() {
        state.isSubmenuOpen = true;
        state.activeContext = 'submenu';
        state.submenuIndex = 0;
        const sub = document.getElementById('retro-popup-sub');
        if (sub) {
            sub.classList.add('open');
        }
        updateSubmenuHighlights();
    }

    /**
     * Zatvorenie kaskádového podmenu
     */
    function closeSubmenu() {
        state.isSubmenuOpen = false;
        state.activeContext = 'menu';
        const sub = document.getElementById('retro-popup-sub');
        if (sub) {
            sub.classList.remove('open');
        }
    }

    /**
     * Obsluha klávesu Escape
     */
    function handleEscKey() {
        // 1. Ak je otvorený modal, zatvor ho
        const openModals = document.querySelectorAll('.retro-modal-overlay.active, .modal-overlay.active, .retro-modal-overlay:not([style*="display: none"])');
        if (openModals.length > 0) {
            openModals.forEach(m => {
                m.style.display = 'none';
                m.classList.remove('active');
            });
            if (window.location.hash.toLowerCase() === "#help") {
                history.replaceState(null, "", window.location.pathname + window.location.search);
            }
            return;
        }

        // 2. Ak je otvorené kaskádové podmenu, zatvor ho
        if (state.isSubmenuOpen) {
            closeSubmenu();
            return;
        }
    }

    /**
     * Prepínanie fokusu na hlavné menu
     */
    function toggleMainMenu() {
        state.activeContext = 'menu';
        updateMenuHighlights();
    }

    /**
     * Vizuálne zvýraznenie vybratej položky v hlavnom menu (azúrový/námornícky pás)
     */
    function updateMenuHighlights() {
        const items = document.querySelectorAll('#retro-main-list .retro-list-item');
        items.forEach(el => {
            const idx = parseInt(el.getAttribute('data-index'), 10);
            if (idx === state.mainMenuIndex) {
                el.classList.add('selected');
                el.scrollIntoView({ block: 'nearest' });
            } else {
                el.classList.remove('selected');
            }
        });
    }

    /**
     * Vizuálne zvýraznenie vybratej položky v kaskádovom podmenu (žiarivý azúrový pás ako na screenshotu!)
     */
    function updateSubmenuHighlights() {
        const items = document.querySelectorAll('#retro-popup-sub .retro-popup-item');
        items.forEach(el => {
            const idx = parseInt(el.getAttribute('data-subindex'), 10);
            if (idx === state.submenuIndex) {
                el.classList.add('active');
            } else {
                el.classList.remove('active');
            }
        });
    }

    /**
     * Prepnutie obrazovky
     */
    window.switchRetroView = function (viewId) {
        // Volanie existujúceho switchView z dashboard.js ak je k dispozícii
        if (typeof window.switchView === 'function') {
            window.switchView(viewId);
        } else {
            document.querySelectorAll('.view-section').forEach(sec => sec.classList.remove('active'));
            const target = document.getElementById(viewId);
            if (target) target.classList.add('active');
        }

        // Aktualizácia titulku okna
        const titleMap = {
            'view-overview': 'PREHĽAD LICENČNÉHO SERVERA',
            'view-licenses': 'EVIDOVANÉ LICENCIE & SÚBEŽNÉ SEDADLÁ',
            'view-keys': 'KRYPTOGRAFICKÉ KĽÚČE & POST-KVANTOVÁ KRYPTOGRAFIA (PQC)',
            'view-relays': 'ON-PREMISE RELAY UZLY S DELEGOVANOU KAPACITOU',
            'view-audit': 'KRYPTOGRAFICKY REŤAZENÝ AUDITNÝ DENNÍK (LEDGER)',
            'view-reports': 'ENTERPRISE AUDIT ANALYTIKA, SÚBEŽNOSŤ & TRUE-UP VÝKAZY (FLT-38)',
            'view-airgap': 'AIR-GAP & OFFLINE LICENČNÝ PORTÁL',
            'view-wasm': 'WEBASSEMBLY & WEBCRYPTO OFFLINE VALIDÁTOR (100% AIR-GAP)',
            'view-webhooks': 'WEBHOOK NOTIFIKÁCIE & HISTÓRIA DORUČENIA',
            'view-mesh': 'RELAY MESH KLASTRE, DISKÉTNY KONSENZUS & HARDWARE ATTESTATION (PHASE 3.0)',
            'view-system': 'CYBER RESILIENCE ACT (CRA) & CYCLONEDX SBOM',
            'view-apikeys': 'SPRÁVA API KĽÚČOV & MERKLE STROM INTEGRITA',
            'view-features': 'GRANULÁRNE MODULY, BALÍKOVÉ SUITY & MERAČE VYŤAŽENIA',
            'view-revocations': 'KRYPTOGRAFICKÝ REVOKAČNÝ ZOZNAM (.SYMRL) & ENTERPRISE CRL',
            'view-queue': 'ENTERPRISE LICENČNÝ RAD & PRIORITNÁ REZERVÁCIA (FLT-31)',
            'view-policy-rules': 'ENTERPRISE OPTIONS PRAVIDLÁ & REZERVÁCIE SEDADIEL (FLT-24)',
            'view-machines': 'NODE-LOCK STROJE & HARDVÉROVÉ FINGERPRINTY (FPR-1 – FPR-19, §8)',
            'view-tokens': 'KREDITOVÉ PEŇAŽENKY & PAY-AS-YOU-GO SPOTREBA',
            'view-migrate': 'MIGRÁCIA Z FLEXNET PUBLISHER (FLEXLM) & KEYGEN.SH',
            'view-scim': 'SCIM 2.0 IDENTITA & FIREMNÁ SYNCHRONIZÁCIA',
            'view-experiments': 'A/B TESTOVANIE & EXPERIMENTAČNÝ ENGINE (AB-1 .. AB-15)',
            'view-pqc': 'POST-QUANTUM ERA SUITE & QUANTUM READINESS AUDIT (M7, §13.5)',
            'view-config': 'KONFIGURÁCIA SYSTÉMU, JAZYK & TÉMY ROZHRANIA',
            'view-help': 'KOMPLEXNÁ PRÍRUČKA & NÁPOVEDA SYSTÉMU ACHILLES (F1)'
        };

        const titleEl = document.getElementById('retro-window-title');
        if (titleEl && titleMap[viewId]) {
            titleEl.textContent = titleMap[viewId];
        }

        if (viewId === 'view-reports' && typeof window.loadReportsView === 'function') {
            window.loadReportsView();
        }
        if (viewId === 'view-queue' && typeof window.loadQueueView === 'function') {
            window.loadQueueView();
        }
        if (viewId === 'view-policy-rules' && typeof window.loadPolicyRulesView === 'function') {
            window.loadPolicyRulesView();
        }
        if (viewId === 'view-machines' && typeof window.loadMachinesView === 'function') {
            window.loadMachinesView();
        }
        if (viewId === 'view-experiments' && typeof window.loadExperimentsView === 'function') {
            window.loadExperimentsView();
        }
        if (viewId === 'view-pqc' && typeof window.loadPqcView === 'function') {
            window.loadPqcView();
        }
        if (viewId === 'view-help' && window.HelpGuide && typeof window.HelpGuide.init === 'function') {
            window.HelpGuide.init();
        }

        // Zatvorenie podmenu pri prepnutí
        closeSubmenu();
    };

    /**
     * Podpora navigácie cez URL hash (#features, #licenses, #keys, atď.)
     */
    function checkHashNavigation() {
        const hash = (window.location.hash || '').replace('#', '').toLowerCase();
        if (!hash) return;

        if (hash === 'features' || hash === 'moduly') {
            switchRetroView('view-features');
            if (typeof window.loadFeaturesView === 'function') window.loadFeaturesView();
        } else if (hash === 'revocations' || hash === 'crl') {
            switchRetroView('view-revocations');
            if (typeof window.loadRevocationsView === 'function') window.loadRevocationsView();
        } else if (hash === 'queue' || hash === 'rad') {
            switchRetroView('view-queue');
            if (typeof window.loadQueueView === 'function') window.loadQueueView();
        } else if (hash === 'rules' || hash === 'policy' || hash === 'options') {
            switchRetroView('view-policy-rules');
            if (typeof window.loadPolicyRulesView === 'function') window.loadPolicyRulesView();
        } else if (hash === 'reports' || hash === 'trueup' || hash === 'reporty') {
            switchRetroView('view-reports');
            if (typeof window.loadReportsView === 'function') window.loadReportsView();
        } else if (hash === 'experiments' || hash === 'view-experiments' || hash === 'ab') {
            switchRetroView('view-experiments');
            if (typeof window.loadExperimentsView === 'function') window.loadExperimentsView();
        } else if (hash === 'exp-report') {
            switchRetroView('view-experiments');
            setTimeout(() => { if (typeof window.showExperimentReport === 'function') window.showExperimentReport('exp-lease-ttl'); }, 300);
        } else if (hash === 'exp-sim') {
            switchRetroView('view-experiments');
            setTimeout(() => { if (typeof window.openSimulateModal === 'function') window.openSimulateModal('exp-lease-ttl'); }, 300);
        } else if (hash === 'pqc' || hash === 'view-pqc' || hash === 'quantum') {
            switchRetroView('view-pqc');
            if (typeof window.loadPqcView === 'function') window.loadPqcView();
        } else if (hash === 'licenses' || hash === 'licencie') {
            switchRetroView('view-licenses');
        } else if (hash === 'keys' || hash === 'kluce') {
            switchRetroView('view-keys');
        } else if (hash === 'tokens' || hash === 'kredity') {
            switchRetroView('view-tokens');
            if (typeof window.loadTokenWallets === 'function') window.loadTokenWallets();
        } else if (hash === 'migrate') {
            switchRetroView('view-migrate');
        } else if (hash === 'webhooks') {
            switchRetroView('view-webhooks');
        } else if (hash === 'audit') {
            switchRetroView('view-audit');
        } else if (hash === 'relays') {
            switchRetroView('view-relays');
        } else if (hash === 'mesh') {
            switchRetroView('view-mesh');
        } else if (hash === 'machines' || hash === 'stroje' || hash === 'nodelock') {
            switchRetroView('view-machines');
            if (typeof window.loadMachinesView === 'function') window.loadMachinesView();
        } else if (hash === 'help' || hash === 'prirucka' || hash === 'guide') {
            switchRetroView('view-help');
        }
    }

    window.addEventListener('hashchange', checkHashNavigation);
    setTimeout(checkHashNavigation, 200);

    /**
     * Kliknutie na položku hlavného menu
     */
    window.handleMainMenuItemClick = function (index) {
        state.mainMenuIndex = index;
        updateMenuHighlights();
        executeCurrentSelection();
    };

    /**
     * Kliknutie na položku podmenu
     */
    window.handleSubmenuItemClick = function (subIndex) {
        state.submenuIndex = subIndex;
        updateSubmenuHighlights();
        executeCurrentSelection();
    };

    /**
     * Otvorenie pomocníka klávesnice
     */
    window.openHelpModal = function () {
        if (typeof window.openModal === 'function') {
            window.openModal('modal-retro-help');
        } else {
            const modal = document.getElementById('modal-retro-help');
            if (modal) {
                modal.classList.add('active');
                modal.style.display = 'flex';
            }
        }
    };

    /**
     * Zatvorenie pomocníka klávesnice
     */
    window.closeHelpModal = function () {
        if (typeof window.closeModal === 'function') {
            window.closeModal('modal-retro-help');
        } else {
            const modal = document.getElementById('modal-retro-help');
            if (modal) {
                modal.classList.remove('active');
                modal.style.display = 'none';
            }
        }
        if (window.location.hash.toLowerCase() === '#help') {
            history.replaceState(null, '', window.location.pathname + window.location.search);
        }
    };

    /**
     * Inicializácia a periodický zber živých telemetrických metrík (CPU, RAM, DISK, NET, IP, Používateľ)
     */
    function initTelemetryPolling() {
        async function fetchTelemetry() {
            try {
                const res = await fetch('/v1/system/telemetry');
                if (res.ok) {
                    const data = await res.json();
                    const cpuEl = document.getElementById('telemetry-cpu');
                    const ramEl = document.getElementById('telemetry-ram');
                    const diskEl = document.getElementById('telemetry-disk');
                    const netEl = document.getElementById('telemetry-net');
                    const ipEl = document.getElementById('telemetry-ip');
                    const userEl = document.getElementById('telemetry-user');

                    if (cpuEl) {
                        cpuEl.textContent = `${data.cpuPercent}%`;
                        cpuEl.style.color = data.cpuPercent > 80 ? '#ff5555' : (data.cpuPercent > 50 ? '#ffaa00' : '#ffff00');
                    }
                    if (ramEl) ramEl.textContent = `${data.memoryMb} MB`;
                    if (diskEl) diskEl.textContent = `${data.diskMb} MB`;
                    if (netEl) netEl.textContent = data.networkActivity || '0 KB/s';
                    if (ipEl) ipEl.textContent = data.serverIp || '127.0.0.1:8080';
                    if (userEl) userEl.textContent = data.currentUser || 'admin:super';
                }
            } catch {
                // Tichý failover pri reštarte servera
            }
        }

        // Aktualizácia hodín každú sekundu
        setInterval(() => {
            const now = new Date();
            const clockEl = document.getElementById('retro-server-clock');
            if (clockEl) {
                clockEl.textContent = now.toLocaleTimeString();
            }
        }, 1000);

        // Okamžité načítanie a následné periodické obnovovanie každé 2 sekundy
        fetchTelemetry();
        setInterval(fetchTelemetry, 2000);
    }

    /**
     * Bezpečné stiahnutie CycloneDX SBOM priamo v tom istom okne bez otvárania nových záložiek
     */
    window.downloadSbomDirect = function () {
        const link = document.createElement('a');
        link.href = '/v1/compliance/sbom';
        link.download = 'symbolon-cyclonedx-sbom.json';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    };

    // Export do window
    window.retroTui = {
        state,
        switchView: window.switchRetroView,
        openHelp: window.openHelpModal,
        closeHelp: window.closeHelpModal,
        closeSubmenu
    };

})();
