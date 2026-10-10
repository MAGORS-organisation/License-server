/**
 * Symbolon Control Plane - Internationalization (i18n) Engine
 * Supported languages: 'sk' (Slovak, default), 'en' (English), 'de' (German)
 */
(function (window) {
    'use strict';

    const translations = {
        sk: {
            // Navigation
            "nav.overview": "Prehľad",
            "nav.licenses": "Licencie",
            "nav.keys": "Podpisové Kľúče",
            "nav.relays": "Relay Uzly",
            "nav.audit": "Auditný Denník",
            "nav.reports": "Reporty & True-Up",
            "nav.airgap": "Air-Gap Portál",
            "nav.webhooks": "Webhooky",
            "nav.apikeys": "API Kľúče & Merkle Tree",
            "nav.mesh": "Relay Mesh & Alerty",
            "nav.scim": "SCIM 2.0 Identity",
            "nav.wasm": "Wasm Validátor",
            "nav.tokens": "Tokeny & Kredity",
            "nav.migrate": "Migrácia (FlexNet)",
            "nav.features": "Moduly & Funkcie",
            "nav.revocations": "Revokácie & CRL",
            "nav.queue": "Licenčný Rad (FLT-31)",
            "nav.policyRules": "Pravidlá & Options (FLT-24)",
            "nav.machines": "Node-Lock & Stroje",
            "nav.experiments": "A/B Experimenty (AB-1)",
            "nav.pqc": "Post-Quantum Era (M7)",
            "nav.system": "API & Diagnostika",
            "nav.config": "Konfigurácia & Jazyk",

            // Topbar
            "topbar.user": "👤 Používateľ",
            "topbar.logout": "🚪 Odhlásiť",
            "topbar.logoutTitle": "Odhlásiť SSO reláciu",
            "topbar.apiKey": "🔑 API Kľúč",
            "topbar.apiKeyTitle": "Nastaviť prístupový API kľúč pre Dashboard",
            "topbar.healthChecking": "Kontrola spojenia...",
            "topbar.healthReady": "Systém pripravený (DB pripojená)",
            "topbar.healthDegraded": "Znížený výkon",
            "topbar.healthError": "Chyba spojenia",
            "topbar.issueLicense": "+ Vystaviť Licenciu",
            "topbar.langTooltip": "Zmeniť jazyk / Switch language / Sprache wechseln",

            // Overview KPIs & Charts
            "overview.floatingSeats": "Floating Sedadlá",
            "overview.ofTotalSeats": "z celkovo {0} sedadiel",
            "overview.capacityUtilization": "Využitie Kapacity",
            "overview.realtimeConcurrent": "konkurentné sedadlá v reálnom čase",
            "overview.deniedRequests": "Zamietnuté Žiadosti",
            "overview.upsellPotential": "potenciál pre upsell sedadiel",
            "overview.activeLicenses": "Aktívne Licencie",
            "overview.allCustomers": "všetkých zákazníkov",
            "overview.signingKeys": "Podpisové Kľúče",
            "overview.activeKeysDesc": "aktívne ES256 / PQC kľúče",
            "overview.chartConcurrency": "Vyťaženie Plávajúcich Sedadiel (Concurrency)",
            "overview.chartTypes": "Rozdelenie Typov Licencií",
            "overview.refresh": "Obnoviť",

            // Common Actions & Labels
            "common.actions": "Akcie",
            "common.status": "Stav",
            "common.active": "Aktívna",
            "common.expired": "Vypršaná",
            "common.revoked": "Revokovaná",
            "common.suspended": "Pozastavená",
            "common.details": "Detail",
            "common.download": "Stiahnuť",
            "common.copy": "Kopírovať",
            "common.copied": "Úspešne skopírované!",
            "common.save": "Uložiť",
            "common.cancel": "Zrušiť",
            "common.close": "Zavrieť",
            "common.create": "Vytvoriť",
            "common.delete": "Zmazať",
            "common.loading": "Načítavam dáta...",
            "common.noRecords": "Žiadne záznamy",
            "common.search": "Vyhľadať...",
            "common.filter": "Filtrovať",
            "common.all": "Všetky",
            "common.success": "Úspech",
            "common.error": "Chyba",
            "common.warning": "Upozornenie",
            "common.confirm": "Potvrdiť",
            "common.product": "Produkt",
            "common.customer": "Zákazník",
            "common.seats": "Sedadlá",
            "common.created": "Vytvorené",
            "common.expires": "Expirácia",
            "common.algorithm": "Algoritmus",
            "common.export": "Exportovať",

            // Configuration View
            "config.pageTitle": "Konfigurácia & Jazykové Nastavenia",
            "config.subtitle": "Nastavenie jazyka používateľského rozhrania, regionálnych formátov, prístupu a parametrov servera.",
            "config.langCardTitle": "🌐 Jazyk Aplikácie (Application Language)",
            "config.langCardDesc": "Vyberte primárny jazyk pre rozhranie riadiaceho centra Symbolon. Zvolený jazyk sa okamžite prejaví a uloží do lokálneho úložiska vášho prehliadača.",
            "config.skName": "Slovenský Jazyk (SK)",
            "config.skDesc": "Predvolená slovenská lokalizácia pre riadenie licencií a kľúčov v stredoeurópskom regióne.",
            "config.enName": "English Language (EN)",
            "config.enDesc": "Full international English localization for global enterprise deployments and DevOps teams.",
            "config.deName": "Nemecký Jazyk (DE)",
            "config.deDesc": "Plnohodnotná nemecká lokalizácia pre Priemysel 4.0, región DACH a podnikové nasadenia.",
            "config.activeTag": "Aktívny Jazyk",
            "config.btnActivateSk": "Prepnúť na Slovenčinu",
            "config.btnActivateEn": "Prepnúť na English",
            "config.btnActivateDe": "Prepnúť na Deutsch",
            "config.localeCardTitle": "📅 Regionálne Formátovanie (Locale & Date/Time)",
            "config.localeCardDesc": "Formát časových pečiatok, číselných údajov a dátumov v reportoch a tabuľkách.",
            "config.optLocaleAuto": "Automaticky podľa zvoleného jazyka (SK/DE: 24h / EN: 12h-24h)",
            "config.optLocaleIso": "Štandardizovaný ISO 8601 (YYYY-MM-DDTHH:mm:ssZ)",
            "config.authCardTitle": "🔑 Autentifikácia & Riadenie Prístupu",
            "config.authCardDesc": "Konfigurácia servisného API kľúča a podnikových SSO integrácií pre prístup k riadiacemu centru.",
            "config.currentApiKeyLabel": "Aktuálny Dashboard API Kľúč:",
            "config.apiKeyConfigured": "Nakonfigurovaný a aktívny",
            "config.apiKeyMissing": "Nenastavený (vyžaduje sa pre administratívne funkcie)",
            "config.btnOpenAuthModal": "Otvoriť Dialóg Autentifikácie",
            "config.systemCardTitle": "⚡ Informácie o Riadiacom Centre",
            "config.versionLabel": "Verzia Riešenia:",
            "config.runtimeLabel": "Platforma Runtime:",
            "config.pqcLabel": "Post-Kvantová Ochrana:",
            "config.pqcValue": "ML-DSA-65 (FIPS 204) + NIST P-256 (ES256)",
            "config.cacheCardTitle": "🧹 Správa Lokálnej Vyrovnávacej Pamäte",
            "config.cacheCardDesc": "Vyčistenie uložených nastavení a dočasných lokálnych dát v prehliadači.",
            "config.btnClearCache": "Vyčistiť Cache Prehliadača",
            "config.cacheClearedMsg": "Lokálne nastavenia boli úspešne vymazané.",
            "config.langSectionTitle": "🌐 Jazyk Rozhrania (Language)",
            "config.langSectionDesc": "Zvoľte jazyk pre administrátorskú konzolu Symbolon:",
            "config.themeCardTitle": "🎨 Vzhľad & Dizajnové Témy (Theme & UI Design)",
            "config.themeCardDesc": "Vyberte primárnu dizajnovú tému pre Symbolon Control Plane. Zmeny sa aplikujú okamžite na všetky pohľady a ukladajú sa do prehliadača.",
            "config.themeModernTitle": "Modern Enterprise",
            "config.themeModernDesc": "Klasický tmavý bridlicový dizajn (Slate-900) optimalizovaný pre produkčné riadiace centrá a monitoring.",
            "config.themeRetroTitle": "Retro FoxPro 2.6 / DOS TUI",
            "config.themeRetroDesc": "Nostalgická kaskádová DOS konzola v štýle 90. rokov s kompletnou podporou ovládania z klávesnice (F1-F10).",
            "config.themeCyberpunkTitle": "Cyberpunk 2077 HUD",
            "config.themeCyberpunkDesc": "Futuristické sci-fi rozhranie s neónovým azúrovým (#00f0ff), žltým a purpurovým žiarením, kybernetickými prvkami a HUD estetikou.",
            "config.themeAppleTitle": "Apple iOS (Human Interface)",
            "config.themeAppleDesc": "Čistý sklenený dizajn (Frosted Glass Glassmorphism) podľa Apple iOS s jemnými tieňmi, SF Pro typografiou a dynamickým denným / nočným režimom.",
            "config.activeThemeTag": "Aktívny Dizajn",
            "config.btnActivateTheme": "Prepnúť Dizajn",
            "theme.appleRegimeLabel": "Režim Apple iOS:",
            "theme.regimeSystem": "Systémový (Auto)",
            "theme.regimeLight": "Denný (Light)",
            "theme.regimeDark": "Nočný (Dark)",
            "config.themeSectionTitle": "🎨 Vzhľad & Téma (Theme)",
            "config.themeSectionDesc": "Zvoľte vizuálny štýl rozhrania Symbolon:",
            "topbar.themeTooltip": "Zmeniť tému rozhrania / Switch visual theme",

            // Licenses View
            "licenses.title": "Prehľad Všetkých Licencií",
            "licenses.colKey": "Licenčný Kľúč",
            "licenses.colProduct": "Produkt",
            "licenses.colCustomer": "Zákazník",
            "licenses.colModel": "Model",
            "licenses.colSeats": "Sedadlá",
            "licenses.colExpires": "Expirácia",
            "licenses.colStatus": "Stav",
            "licenses.searchPlaceholder": "Filtrovať podľa kľúča alebo zákazníka...",
            "licenses.btnIssue": "+ Vystaviť Licenciu",

            // Signing Keys View
            "keys.title": "Kryptografické Podpisové Kľúče (KMS & HSM)",
            "keys.colKid": "Key ID (KID)",
            "keys.colAlgorithm": "Algoritmus",
            "keys.colUsage": "Účel",
            "keys.colCreated": "Vytvorený",
            "keys.colStatus": "Stav",
            "keys.btnRotate": "🔄 Rotovať Kľúč",
            "keys.btnJwks": "🌐 Zobraziť JWKS",

            // Relays View
            "relays.title": "Relay Inštancie & Lokálne Proxy Servery",
            "relays.colName": "Názov Relay",
            "relays.colEndpoint": "Endpoint URL",
            "relays.colAssignedSeats": "Priradené Sedadlá",
            "relays.colLastHeartbeat": "Posledný Heartbeat",

            // Audit View
            "audit.title": "Nemenný Auditný Denník (Tamper-Evident Hash Chain)",
            "audit.colTime": "Čas",
            "audit.colType": "Typ Udalosti",
            "audit.colLicense": "Licencia",
            "audit.colFingerprint": "Fingerprint",
            "audit.colHash": "Kryptografický Hash",

            // Reports View
            "reports.title": "Reporty Využitia & True-Up Vyúčtovanie",
            "reports.btnGenerate": "Generovať Report",

            // Air-Gap View
            "airgap.title": "Air-Gap Offline Aktivácia & Spracovanie Požiadaviek",
            "airgap.dropzoneText": "Presuňte súbor .symreq sem alebo kliknite pre výber",
            "airgap.btnProcess": "Spracovať Požiadavku (.symreq)",

            // Webhooks View
            "webhooks.title": "Webhook Integrácie & Notifikácie Udalostí",
            "webhooks.btnCreate": "+ Pridať Webhook",

            // API Keys View
            "apikeys.title": "Správa API Kľúčov & Merkle Tree Overenie",
            "apikeys.btnCreate": "+ Nový API Kľúč",

            // Mesh View
            "mesh.title": "Relay Mesh Koordinácia & Georeplikácia",

            // SCIM View
            "scim.title": "SCIM 2.0 Podniková Synchronizácia Používateľov",

            // Wasm View
            "wasm.title": "Klientsky WebAssembly & WebCrypto Validátor",

            // Tokens View
            "tokens.title": "Tokenové Peňaženky & Kreditový Model",

            // Migrate View
            "migrate.title": "Migračný Nástroj z FlexNet / Reprise RLM",

            // Features View
            "features.title": "Katalóg Modulov, Balíčkov & Funkcií",
            "features.btnCreate": "+ Pridať Funkciu",

            // Revocations View
            "revocations.title": "Revokačné Zoznamy & Blacklist",
            "revocations.btnRevoke": "⛔ Vytvoriť Revokáciu",

            // Queue View
            "queue.title": "Správa Licenčného Radu (Fair Queuing & Priority)",

            // Policy Rules View
            "rules.title": "Pravidlá Prístupu & Options Súbory (YAML)",

            // Machines View
            "machines.title": "Node-Lock Aktivácie & Hardvérové Stroje",

            // Experiments View
            "experiments.title": "A/B Experimentovanie & Canary Rollouty",
            "experiments.btnNew": "+ Nový Experiment",

            // PQC View
            "pqc.title": "Post-Kvantové Kryptografické Podpisy (ML-DSA-65)",

            // System View
            "system.title": "API Špecifikácia & Diagnostika",

            // Modals
            "modal.authTitle": "Autentifikácia & Prístup do Správy",
            "modal.issueTitle": "Vystavenie Novej Licencie",
            "modal.rotateTitle": "Rotácia Podpisového Kľúča",
            "modal.detailsTitle": "Detail Licencie",
            "modal.successTitle": "Licencia Úspešne Vystavená!",
            "modal.apiKeySave": "Uložiť a Použiť",
            "modal.apiKeyClear": "Odstrániť Kľúč"
        },
        en: {
            // Navigation
            "nav.overview": "Overview",
            "nav.licenses": "Licenses",
            "nav.keys": "Signing Keys",
            "nav.relays": "Relay Nodes",
            "nav.audit": "Audit Log",
            "nav.reports": "Reports & True-Up",
            "nav.airgap": "Air-Gap Portal",
            "nav.webhooks": "Webhooks",
            "nav.apikeys": "API Keys & Merkle Tree",
            "nav.mesh": "Relay Mesh & Alerts",
            "nav.scim": "SCIM 2.0 Identity",
            "nav.wasm": "Wasm Validator",
            "nav.tokens": "Tokens & Credits",
            "nav.migrate": "Migration (FlexNet)",
            "nav.features": "Modules & Features",
            "nav.revocations": "Revocations & CRL",
            "nav.queue": "License Queue (FLT-31)",
            "nav.policyRules": "Rules & Options (FLT-24)",
            "nav.machines": "Node-Lock & Machines",
            "nav.experiments": "A/B Experiments (AB-1)",
            "nav.pqc": "Post-Quantum Era (M7)",
            "nav.system": "API & Diagnostics",
            "nav.config": "Configuration & Language",

            // Topbar
            "topbar.user": "👤 User",
            "topbar.logout": "🚪 Sign Out",
            "topbar.logoutTitle": "Sign out of SSO session",
            "topbar.apiKey": "🔑 API Key",
            "topbar.apiKeyTitle": "Set dashboard access API key",
            "topbar.healthChecking": "Checking connection...",
            "topbar.healthReady": "System Ready (DB Connected)",
            "topbar.healthDegraded": "Degraded Performance",
            "topbar.healthError": "Connection Error",
            "topbar.issueLicense": "+ Issue License",
            "topbar.langTooltip": "Switch language / Zmeniť jazyk / Sprache wechseln",

            // Overview KPIs & Charts
            "overview.floatingSeats": "Floating Seats",
            "overview.ofTotalSeats": "of total {0} seats",
            "overview.capacityUtilization": "Capacity Utilization",
            "overview.realtimeConcurrent": "real-time concurrent seats",
            "overview.deniedRequests": "Denied Requests",
            "overview.upsellPotential": "seat upsell potential",
            "overview.activeLicenses": "Active Licenses",
            "overview.allCustomers": "across all customers",
            "overview.signingKeys": "Signing Keys",
            "overview.activeKeysDesc": "active ES256 / PQC keys",
            "overview.chartConcurrency": "Floating Seat Utilization (Concurrency)",
            "overview.chartTypes": "License Type Breakdown",
            "overview.refresh": "Refresh",

            // Common Actions & Labels
            "common.actions": "Actions",
            "common.status": "Status",
            "common.active": "Active",
            "common.expired": "Expired",
            "common.revoked": "Revoked",
            "common.suspended": "Suspended",
            "common.details": "Details",
            "common.download": "Download",
            "common.copy": "Copy",
            "common.copied": "Successfully copied!",
            "common.save": "Save",
            "common.cancel": "Cancel",
            "common.close": "Close",
            "common.create": "Create",
            "common.delete": "Delete",
            "common.loading": "Loading data...",
            "common.noRecords": "No records found",
            "common.search": "Search...",
            "common.filter": "Filter",
            "common.all": "All",
            "common.success": "Success",
            "common.error": "Error",
            "common.warning": "Warning",
            "common.confirm": "Confirm",
            "common.product": "Product",
            "common.customer": "Customer",
            "common.seats": "Seats",
            "common.created": "Created",
            "common.expires": "Expiration",
            "common.algorithm": "Algorithm",
            "common.export": "Export",

            // Configuration View
            "config.pageTitle": "Configuration & Language Settings",
            "config.subtitle": "Manage application user interface language, regional formats, access keys, and server parameters.",
            "config.langCardTitle": "🌐 UI Language & Localization",
            "config.langCardDesc": "Choose your primary language for the Symbolon Control Plane dashboard. The selected language applies instantly across all views and persists in your browser.",
            "config.skName": "Slovak Language (SK)",
            "config.skDesc": "Native Central European Slovak localization for license, seat, and key governance.",
            "config.enName": "English Language (EN)",
            "config.enDesc": "Full international English localization for global enterprise deployments and DevOps teams.",
            "config.deName": "German Language (DE)",
            "config.deDesc": "Comprehensive German localization for Industry 4.0, DACH region, and enterprise environments.",
            "config.activeTag": "Active Language",
            "config.btnActivateSk": "Switch to Slovak",
            "config.btnActivateEn": "Switch to English",
            "config.btnActivateDe": "Switch to German",
            "config.localeCardTitle": "📅 Regional Formatting (Locale & Date/Time)",
            "config.localeCardDesc": "Format of timestamps, numbers, and dates displayed across tables and audit logs.",
            "config.optLocaleAuto": "Automatic based on active language (SK/DE: 24h / EN: 12h-24h)",
            "config.optLocaleIso": "Standardized ISO 8601 (YYYY-MM-DDTHH:mm:ssZ)",
            "config.authCardTitle": "🔑 Authentication & Access Control",
            "config.authCardDesc": "Configure service API key and enterprise SSO identity providers for administrative access.",
            "config.currentApiKeyLabel": "Current Dashboard API Key:",
            "config.apiKeyConfigured": "Configured and active",
            "config.apiKeyMissing": "Not configured (required for administrative operations)",
            "config.btnOpenAuthModal": "Open Authentication Modal",
            "config.systemCardTitle": "⚡ Control Plane Runtime Info",
            "config.versionLabel": "Solution Version:",
            "config.runtimeLabel": "Platform Runtime:",
            "config.pqcLabel": "Quantum Defense:",
            "config.pqcValue": "ML-DSA-65 (FIPS 204) + NIST P-256 (ES256)",
            "config.cacheCardTitle": "🧹 Local Browser Storage Management",
            "config.cacheCardDesc": "Purge cached local settings and credentials stored in browser storage.",
            "config.btnClearCache": "Purge Local Storage",
            "config.cacheClearedMsg": "Local storage settings purged successfully.",
            "config.langSectionTitle": "🌐 Interface Language",
            "config.langSectionDesc": "Select your preferred language for Symbolon console:",
            "config.themeCardTitle": "🎨 Interface Themes & Visual Design",
            "config.themeCardDesc": "Select the primary visual theme for Symbolon Control Plane. Changes apply immediately across all views and persist in your browser's local storage.",
            "config.themeModernTitle": "Modern Enterprise",
            "config.themeModernDesc": "Classic dark slate theme (Slate-900) optimized for production control planes and 24/7 monitoring.",
            "config.themeRetroTitle": "Retro FoxPro 2.6 / DOS TUI",
            "config.themeRetroDesc": "Nostalgic 1990s cascading DOS console with full keyboard navigation and shortcut support (F1-F10).",
            "config.themeCyberpunkTitle": "Cyberpunk 2077 HUD",
            "config.themeCyberpunkDesc": "Futuristic sci-fi HUD interface with neon cyan (#00f0ff), yellow and magenta glow, cybernetic borders and night city aesthetic.",
            "config.themeAppleTitle": "Apple iOS (Human Interface)",
            "config.themeAppleDesc": "Clean frosted glass glassmorphism design following Apple iOS Human Interface Guidelines, SF Pro typography, and dynamic day/night regime.",
            "config.activeThemeTag": "Active Design",
            "config.btnActivateTheme": "Switch Theme",
            "theme.appleRegimeLabel": "Apple iOS Regime:",
            "theme.regimeSystem": "System (Auto)",
            "theme.regimeLight": "Day (Light)",
            "theme.regimeDark": "Night (Dark)",
            "config.themeSectionTitle": "🎨 Interface Theme & Visual Design",
            "config.themeSectionDesc": "Choose your preferred visual style for the Symbolon console:",
            "topbar.themeTooltip": "Switch visual theme / Zmeniť tému rozhrania",

            // Licenses View
            "licenses.title": "All Licenses Overview",
            "licenses.colKey": "License Key",
            "licenses.colProduct": "Product",
            "licenses.colCustomer": "Customer",
            "licenses.colModel": "Model",
            "licenses.colSeats": "Seats",
            "licenses.colExpires": "Expiration",
            "licenses.colStatus": "Status",
            "licenses.searchPlaceholder": "Filter by key or customer...",
            "licenses.btnIssue": "+ Issue License",

            // Signing Keys View
            "keys.title": "Cryptographic Signing Keys (KMS & HSM)",
            "keys.colKid": "Key ID (KID)",
            "keys.colAlgorithm": "Algorithm",
            "keys.colUsage": "Usage",
            "keys.colCreated": "Created",
            "keys.colStatus": "Status",
            "keys.btnRotate": "🔄 Rotate Key",
            "keys.btnJwks": "🌐 View JWKS",

            // Relays View
            "relays.title": "Relay Instances & Local Proxy Servers",
            "relays.colName": "Relay Name",
            "relays.colEndpoint": "Endpoint URL",
            "relays.colAssignedSeats": "Assigned Seats",
            "relays.colLastHeartbeat": "Last Heartbeat",

            // Audit View
            "audit.title": "Immutable Audit Log (Tamper-Evident Hash Chain)",
            "audit.colTime": "Timestamp",
            "audit.colType": "Event Type",
            "audit.colLicense": "License",
            "audit.colFingerprint": "Fingerprint",
            "audit.colHash": "Cryptographic Hash",

            // Reports View
            "reports.title": "Usage Reports & True-Up Billing",
            "reports.btnGenerate": "Generate Report",

            // Air-Gap View
            "airgap.title": "Air-Gap Offline Activation & Processing",
            "airgap.dropzoneText": "Drag and drop .symreq file here or click to browse",
            "airgap.btnProcess": "Process Request (.symreq)",

            // Webhooks View
            "webhooks.title": "Webhook Integrations & Event Notifications",
            "webhooks.btnCreate": "+ Add Webhook",

            // API Keys View
            "apikeys.title": "API Key Governance & Merkle Tree Verification",
            "apikeys.btnCreate": "+ New API Key",

            // Mesh View
            "mesh.title": "Relay Mesh Coordination & Geo-Replication",

            // SCIM View
            "scim.title": "SCIM 2.0 Enterprise User Synchronization",

            // Wasm View
            "wasm.title": "Client-Side WebAssembly & WebCrypto Validator",

            // Tokens View
            "tokens.title": "Token Wallets & Credit Consumption Model",

            // Migrate View
            "migrate.title": "FlexNet / Reprise RLM Migration Engine",

            // Features View
            "features.title": "Modules, Packages & Features Catalog",
            "features.btnCreate": "+ Add Feature",

            // Revocations View
            "revocations.title": "Revocation Lists & CRL Blacklist",
            "revocations.btnRevoke": "⛔ Create Revocation",

            // Queue View
            "queue.title": "License Queue Management (Fair Queuing & Priority)",

            // Policy Rules View
            "rules.title": "Access Policy Rules & Options Files (YAML)",

            // Machines View
            "machines.title": "Node-Lock Activations & Machine Hardware",

            // Experiments View
            "experiments.title": "A/B Experimentation & Canary Rollouts",
            "experiments.btnNew": "+ New Experiment",

            // PQC View
            "pqc.title": "Post-Quantum Cryptographic Signatures (ML-DSA-65)",

            // System View
            "system.title": "API Specification & Diagnostics",

            // Modals
            "modal.authTitle": "Authentication & Access Management",
            "modal.issueTitle": "Issue New License",
            "modal.rotateTitle": "Rotate Signing Key",
            "modal.detailsTitle": "License Details",
            "modal.successTitle": "License Successfully Issued!",
            "modal.apiKeySave": "Save & Apply",
            "modal.apiKeyClear": "Remove Key"
        },
        de: {
            // Navigation
            "nav.overview": "Übersicht",
            "nav.licenses": "Lizenzen",
            "nav.keys": "Signaturschlüssel",
            "nav.relays": "Relay-Knoten",
            "nav.audit": "Audit-Protokoll",
            "nav.reports": "Berichte & True-Up",
            "nav.airgap": "Air-Gap-Portal",
            "nav.webhooks": "Webhooks",
            "nav.apikeys": "API-Schlüssel & Merkle-Baum",
            "nav.mesh": "Relay-Mesh & Alarme",
            "nav.scim": "SCIM 2.0 Identitäten",
            "nav.wasm": "Wasm-Validator",
            "nav.tokens": "Token & Guthaben",
            "nav.migrate": "Migration (FlexNet)",
            "nav.features": "Module & Funktionen",
            "nav.revocations": "Widerrufe & CRL",
            "nav.queue": "Lizenz-Warteschlange (FLT-31)",
            "nav.policyRules": "Richtlinien & Optionen (FLT-24)",
            "nav.machines": "Node-Lock & Maschinen",
            "nav.experiments": "A/B-Experimente (AB-1)",
            "nav.pqc": "Post-Quanten-Ära (M7)",
            "nav.system": "API & Diagnose",
            "nav.config": "Konfiguration & Sprache",

            // Topbar
            "topbar.user": "👤 Benutzer",
            "topbar.logout": "🚪 Abmelden",
            "topbar.logoutTitle": "SSO-Sitzung abmelden",
            "topbar.apiKey": "🔑 API-Schlüssel",
            "topbar.apiKeyTitle": "Dashboard-API-Zugriffsschlüssel festlegen",
            "topbar.healthChecking": "Verbindung wird geprüft...",
            "topbar.healthReady": "System bereit (DB verbunden)",
            "topbar.healthDegraded": "Eingeschränkte Leistung",
            "topbar.healthError": "Verbindungsfehler",
            "topbar.issueLicense": "+ Lizenz ausstellen",
            "topbar.langTooltip": "Sprache wechseln / Switch language / Zmeniť jazyk",

            // Overview KPIs & Charts
            "overview.floatingSeats": "Floating-Sitze",
            "overview.ofTotalSeats": "von insgesamt {0} Sitzen",
            "overview.capacityUtilization": "Kapazitätsauslastung",
            "overview.realtimeConcurrent": "gleichzeitige Sitze in Echtzeit",
            "overview.deniedRequests": "Abgelehnte Anfragen",
            "overview.upsellPotential": "Potenzial für Sitz-Upselling",
            "overview.activeLicenses": "Aktive Lizenzen",
            "overview.allCustomers": "aller Kunden",
            "overview.signingKeys": "Signaturschlüssel",
            "overview.activeKeysDesc": "aktive ES256 / PQC-Schlüssel",
            "overview.chartConcurrency": "Auslastung der Floating-Sitze (Parallelität)",
            "overview.chartTypes": "Lizenztypen-Verteilung",
            "overview.refresh": "Aktualisieren",

            // Common Actions & Labels
            "common.actions": "Aktionen",
            "common.status": "Status",
            "common.active": "Aktiv",
            "common.expired": "Abgelaufen",
            "common.revoked": "Widerrufen",
            "common.suspended": "Ausgesetzt",
            "common.details": "Details",
            "common.download": "Herunterladen",
            "common.copy": "Kopieren",
            "common.copied": "Erfolgreich kopiert!",
            "common.save": "Speichern",
            "common.cancel": "Abbrechen",
            "common.close": "Schließen",
            "common.create": "Erstellen",
            "common.delete": "Löschen",
            "common.loading": "Daten werden geladen...",
            "common.noRecords": "Keine Einträge gefunden",
            "common.search": "Suchen...",
            "common.filter": "Filtern",
            "common.all": "Alle",
            "common.success": "Erfolg",
            "common.error": "Fehler",
            "common.warning": "Warnung",
            "common.confirm": "Bestätigen",
            "common.product": "Produkt",
            "common.customer": "Kunde",
            "common.seats": "Sitze",
            "common.created": "Erstellt",
            "common.expires": "Ablauf",
            "common.algorithm": "Algorithmus",
            "common.export": "Exportieren",

            // Configuration View
            "config.pageTitle": "Konfiguration & Spracheinstellungen",
            "config.subtitle": "Verwalten Sie die Sprache der Benutzeroberfläche, regionale Formate, Zugriffsschlüssel und Serverparameter.",
            "config.langCardTitle": "🌐 Anwendungssprache (Application Language)",
            "config.langCardDesc": "Wählen Sie Ihre bevorzugte Sprache für das Symbolon Control Plane Dashboard. Die Auswahl wird sofort wirksam und im Browser gespeichert.",
            "config.skName": "Slowakische Sprache (SK)",
            "config.skDesc": "Ursprüngliche mitteleuropäische Lokalisierung für Lizenz-, Sitz- und Schlüsselverwaltung.",
            "config.enName": "Englische Sprache (EN)",
            "config.enDesc": "Internationale englische Version für globale Enterprise-Bereitstellungen und DevOps-Teams.",
            "config.deName": "Deutsche Sprache (DE)",
            "config.deDesc": "Umfassende deutsche Lokalisierung für Industrie 4.0, DACH-Region und Enterprise-Umgebungen.",
            "config.activeTag": "Aktive Sprache",
            "config.btnActivateSk": "Zu Slowakisch wechseln",
            "config.btnActivateEn": "Zu Englisch wechseln",
            "config.btnActivateDe": "Zu Deutsch wechseln",
            "config.localeCardTitle": "📅 Regionale Formatierung (Locale & Datum/Uhrzeit)",
            "config.localeCardDesc": "Format von Zeitstempeln, Zahlen und Datumsangaben in Berichten und Tabellen.",
            "config.optLocaleAuto": "Automatisch nach gewählter Sprache (SK/DE: 24h / EN: 12h-24h)",
            "config.optLocaleIso": "Standardisiertes ISO 8601 (YYYY-MM-DDTHH:mm:ssZ)",
            "config.authCardTitle": "🔑 Authentifizierung & Zugriffsverwaltung",
            "config.authCardDesc": "Konfiguration von Service-API-Schlüsseln und Enterprise-SSO für den Administratorzugriff.",
            "config.currentApiKeyLabel": "Aktueller Dashboard-API-Schlüssel:",
            "config.apiKeyConfigured": "Konfiguriert und aktiv",
            "config.apiKeyMissing": "Nicht konfiguriert (erforderlich für administrative Funktionen)",
            "config.btnOpenAuthModal": "Authentifizierungsdialog öffnen",
            "config.systemCardTitle": "⚡ Control-Plane-Laufzeitinformationen",
            "config.versionLabel": "Lösungsversion:",
            "config.runtimeLabel": "Laufzeitplattform:",
            "config.pqcLabel": "Quantensicherheit:",
            "config.pqcValue": "ML-DSA-65 (FIPS 204) + NIST P-256 (ES256)",
            "config.cacheCardTitle": "🧹 Lokale Browserspeicher-Verwaltung",
            "config.cacheCardDesc": "Löschen von zwischengespeicherten lokalen Einstellungen und temporären Daten im Browser.",
            "config.btnClearCache": "Lokalen Speicher leeren",
            "config.cacheClearedMsg": "Lokale Speichereinstellungen wurden erfolgreich geleert.",
            "config.langSectionTitle": "🌐 Benutzeroberflächensprache (Language)",
            "config.langSectionDesc": "Wählen Sie Ihre bevorzugte Sprache für die Symbolon-Konsole:",
            "config.themeCardTitle": "🎨 Erscheinungsbild & Design-Themes",
            "config.themeCardDesc": "Wählen Sie das visuelle Hauptdesign für Symbolon Control Plane. Änderungen werden sofort auf alle Ansichten angewendet und im lokalen Browserspeicher gespeichert.",
            "config.themeModernTitle": "Modern Enterprise",
            "config.themeModernDesc": "Klassisches dunkles Schiefer-Design (Slate-900), optimiert für Produktionsleitstände und 24/7-Monitoring.",
            "config.themeRetroTitle": "Retro FoxPro 2.6 / DOS TUI",
            "config.themeRetroDesc": "Nostalgische DOS-Kaskadenkonsole im 90er-Jahre-Stil mit vollständiger Tastaturnavigation (F1-F10).",
            "config.themeCyberpunkTitle": "Cyberpunk 2077 HUD",
            "config.themeCyberpunkDesc": "Futuristisches Sci-Fi-HUD-Design mit leuchtendem Cyan (#00f0ff), Gelb und Magenta, kybernetischen Rändern und Night-City-Ästhetik.",
            "config.themeAppleTitle": "Apple iOS (Human Interface)",
            "config.themeAppleDesc": "Elegantes Frosted-Glass-Design nach Apple iOS Human Interface Guidelines mit SF Pro-Typografie und dynamischem Tag-/Nacht-Modus.",
            "config.activeThemeTag": "Aktives Design",
            "config.btnActivateTheme": "Design aktivieren",
            "theme.appleRegimeLabel": "Apple iOS Modus:",
            "theme.regimeSystem": "System (Auto)",
            "theme.regimeLight": "Tag (Hell)",
            "theme.regimeDark": "Nacht (Dunkel)",
            "config.themeSectionTitle": "🎨 Erscheinungsbild & Design-Theme",
            "config.themeSectionDesc": "Wählen Sie Ihren bevorzugten visuellen Stil für die Symbolon-Konsole:",
            "topbar.themeTooltip": "Visuelles Design wechseln / Switch theme",

            // Licenses View
            "licenses.title": "Übersicht aller Lizenzen",
            "licenses.colKey": "Lizenzschlüssel",
            "licenses.colProduct": "Produkt",
            "licenses.colCustomer": "Kunde",
            "licenses.colModel": "Modell",
            "licenses.colSeats": "Sitze",
            "licenses.colExpires": "Ablauf",
            "licenses.colStatus": "Status",
            "licenses.searchPlaceholder": "Nach Schlüssel oder Kunde filtern...",
            "licenses.btnIssue": "+ Lizenz ausstellen",

            // Signing Keys View
            "keys.title": "Kryptografische Signaturschlüssel (KMS & HSM)",
            "keys.colKid": "Key-ID (KID)",
            "keys.colAlgorithm": "Algorithmus",
            "keys.colUsage": "Verwendungszweck",
            "keys.colCreated": "Erstellt",
            "keys.colStatus": "Status",
            "keys.btnRotate": "🔄 Schlüssel rotieren",
            "keys.btnJwks": "🌐 JWKS anzeigen",

            // Relays View
            "relays.title": "Relay-Instanzen & Lokale Proxy-Server",
            "relays.colName": "Relay-Name",
            "relays.colEndpoint": "Endpoint-URL",
            "relays.colAssignedSeats": "Zugewiesene Sitze",
            "relays.colLastHeartbeat": "Letzter Heartbeat",

            // Audit View
            "audit.title": "Unveränderliches Audit-Protokoll (Tamper-Evident Hash Chain)",
            "audit.colTime": "Zeitpunkt",
            "audit.colType": "Ereignistyp",
            "audit.colLicense": "Lizenz",
            "audit.colFingerprint": "Fingerprint",
            "audit.colHash": "Kryptografischer Hash",

            // Reports View
            "reports.title": "Nutzungsberichte & True-Up-Abrechnung",
            "reports.btnGenerate": "Bericht generieren",

            // Air-Gap View
            "airgap.title": "Air-Gap-Offline-Aktivierung & Anfrageverarbeitung",
            "airgap.dropzoneText": ".symreq-Datei hierher ziehen oder zum Auswählen klicken",
            "airgap.btnProcess": "Anfrage verarbeiten (.symreq)",

            // Webhooks View
            "webhooks.title": "Webhook-Integrationen & Ereignisbenachrichtigungen",
            "webhooks.btnCreate": "+ Webhook hinzufügen",

            // API Keys View
            "apikeys.title": "API-Schlüsselverwaltung & Merkle-Baum-Verifizierung",
            "apikeys.btnCreate": "+ Neuer API-Schlüssel",

            // Mesh View
            "mesh.title": "Relay-Mesh-Koordination & Georeplikation",

            // SCIM View
            "scim.title": "SCIM 2.0 Enterprise-Benutzersynchronisierung",

            // Wasm View
            "wasm.title": "Clientseitiger WebAssembly- & WebCrypto-Validator",

            // Tokens View
            "tokens.title": "Token-Wallets & Guthaben-Verbrauchsmodell",

            // Migrate View
            "migrate.title": "Migrations-Engine für FlexNet / Reprise RLM",

            // Features View
            "features.title": "Katalog für Module, Pakete & Funktionen",
            "features.btnCreate": "+ Funktion hinzufügen",

            // Revocations View
            "revocations.title": "Widerrufslisten & CRL-Blacklist",
            "revocations.btnRevoke": "⛔ Widerruf erstellen",

            // Queue View
            "queue.title": "Lizenz-Warteschlangenverwaltung (Fair Queuing & Priorität)",

            // Policy Rules View
            "rules.title": "Zugriffsrichtlinien & Options-Dateien (YAML)",

            // Machines View
            "machines.title": "Node-Lock-Aktivierungen & Hardware-Maschinen",

            // Experiments View
            "experiments.title": "A/B-Experimente & Canary-Rollouts",
            "experiments.btnNew": "+ Neues Experiment",

            // PQC View
            "pqc.title": "Post-Quanten-Kryptografiesignaturen (ML-DSA-65)",

            // System View
            "system.title": "API-Spezifikation & Diagnose",

            // Modals
            "modal.authTitle": "Authentifizierung & Zugriffsverwaltung",
            "modal.issueTitle": "Neue Lizenz ausstellen",
            "modal.rotateTitle": "Signaturschlüssel rotieren",
            "modal.detailsTitle": "Lizenzdetails",
            "modal.successTitle": "Lizenz erfolgreich ausgestellt!",
            "modal.apiKeySave": "Speichern & Anwenden",
            "modal.apiKeyClear": "Schlüssel entfernen"
        }
    };

    const STORAGE_KEY = "symbolon_lang";
    let currentLang = "sk";

    function getLanguage() {
        const stored = localStorage.getItem(STORAGE_KEY);
        if (stored === "en" || stored === "sk" || stored === "de") {
            return stored;
        }
        return "sk";
    }

    function t(key, fallback = "") {
        const lang = currentLang;
        if (translations[lang] && translations[lang][key] !== undefined) {
            return translations[lang][key];
        }
        if (translations["sk"] && translations["sk"][key] !== undefined) {
            return translations["sk"][key];
        }
        return fallback || key;
    }

    function applyTranslations() {
        document.documentElement.lang = currentLang;

        // Text / HTML content
        document.querySelectorAll("[data-i18n]").forEach(el => {
            const key = el.getAttribute("data-i18n");
            if (key) {
                const text = t(key);
                if (text) {
                    el.innerHTML = text;
                }
            }
        });

        // Placeholders
        document.querySelectorAll("[data-i18n-placeholder]").forEach(el => {
            const key = el.getAttribute("data-i18n-placeholder");
            if (key) {
                const text = t(key);
                if (text) {
                    el.placeholder = text;
                }
            }
        });

        // Titles / Tooltips
        document.querySelectorAll("[data-i18n-title]").forEach(el => {
            const key = el.getAttribute("data-i18n-title");
            if (key) {
                const text = t(key);
                if (text) {
                    el.title = text;
                }
            }
        });

        // Update Topbar and Modal language toggle buttons
        document.querySelectorAll(".lang-btn").forEach(btn => {
            const btnLang = btn.getAttribute("data-lang");
            if (btnLang === currentLang) {
                btn.classList.add("active");
            } else {
                btn.classList.remove("active");
            }
        });

        // Update Retro Topbar language links
        document.querySelectorAll(".retro-lang-link").forEach(link => {
            link.classList.toggle("active", link.getAttribute("data-lang") === currentLang);
        });

        // Update Config View language card active states if present
        const skCard = document.getElementById("lang-card-sk");
        const enCard = document.getElementById("lang-card-en");
        const deCard = document.getElementById("lang-card-de");
        const skTag = document.getElementById("tag-active-sk");
        const enTag = document.getElementById("tag-active-en");
        const deTag = document.getElementById("tag-active-de");

        if (skCard) skCard.classList.toggle("border-active", currentLang === "sk");
        if (enCard) enCard.classList.toggle("border-active", currentLang === "en");
        if (deCard) deCard.classList.toggle("border-active", currentLang === "de");

        if (skTag) skTag.style.display = currentLang === "sk" ? "inline-block" : "none";
        if (enTag) enTag.style.display = currentLang === "en" ? "inline-block" : "none";
        if (deTag) deTag.style.display = currentLang === "de" ? "inline-block" : "none";

        // Update Page Title if an active nav link exists
        const activeNavLink = document.querySelector(".nav-link.active");
        if (activeNavLink) {
            const titleSpan = activeNavLink.querySelector("span:not(.nav-icon)");
            if (titleSpan) {
                const pageTitleEl = document.getElementById("page-title");
                if (pageTitleEl) {
                    pageTitleEl.textContent = titleSpan.textContent.trim();
                }
            }
        }

        // Notify other components
        window.dispatchEvent(new CustomEvent("symbolon:languageChanged", { detail: { lang: currentLang } }));
    }

    function setLanguage(lang) {
        if (lang !== "sk" && lang !== "en" && lang !== "de") {
            lang = "sk";
        }
        currentLang = lang;
        localStorage.setItem(STORAGE_KEY, lang);
        applyTranslations();
    }

    // Initialize on page load
    function initI18n() {
        currentLang = getLanguage();
        applyTranslations();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initI18n);
    } else {
        initI18n();
    }

    // Expose Global I18N API
    window.I18N = {
        getLanguage,
        setLanguage,
        t,
        applyTranslations,
        translations
    };

    window.setAppLanguage = function (lang) {
        setLanguage(lang);
    };

})(window);
