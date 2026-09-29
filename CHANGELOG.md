# Zoznam Zmien (Changelog)

Všetky podstatné zmeny v projekte **Symbolon Enterprise Floating License Server** sú dokumentované v tomto súbore.

Formát vychádza zo špecifikácie [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
a projekt striktne dodržiava [Sémantické Verziovanie (Semantic Versioning 2.0.0)](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Pridané (Added)
- **Enterprise License Priority Queueing, Reaper Service & Client Auto-Wait (FLT-31, §7.1, §7.2, §7.5)**:
  - **Dátová a protokolová vrstva (`Symbolon.Protocol`, `Symbolon.Data`)**:
    - DTO kontrakty `QueuedResponseDto`, `QueueStatusResponseDto`, `CheckoutRequestDto`, `QueueTicketItemDto` s podporou priority (`Priority`), `Status`, `RetryAfterSeconds` a registrácia v `SymbolonProtocolJsonContext` pre AOT kompiláciu.
    - Entita `QueueTicketEntity` s prioritným atribútom a kompozitným indexom `(LicenseId, Status, Priority, CreatedAt)` v `SymbolonDbContext`.
  - **Serverové riadenie fronty (`Symbolon.ControlPlane`)**:
    - `QueueManager`: Prioritné zaraďovanie (`OrderByDescending(Priority).ThenBy(CreatedAt)`), dynamický výpočet pozície v rade, odhadovaná doba čakania (`estimatedWait`), manuálna aj automatická propagácia sedadla (`PromoteTicketAsync`, `TryPromoteNextAsync`), webhook udalosť `queue.promoted`.
    - `QueueReaperBackgroundService`: Pravidelné čistenie expirovaných lístkov a vyhodnocovanie čakacích radov na pozadí každých 5s.
    - REST endpointy: `CheckoutAsync` (vracia `202 Accepted` s `Retry-After` hlavičkou pri vyčerpanej kapacite), `ReleaseAsync` (automaticky propaguje ďalší lístok v rade pri uvoľnení sedadla), `GET /v1/queue/{ticket}`, `DELETE /v1/queue/{ticket}`.
    - Admin API: `GET /admin/v1/queue`, `POST /admin/v1/queue/{ticket}/promote`, `DELETE /admin/v1/queue/{ticket}`.
  - **Edge Relay Server Local Queueing (`Symbolon.Relay`)**:
    - `RelayQueueManager`: Lokálny in-memory prioritný manažér fronty pre offline/edge prevádzku s podporou propagácie a pozícií.
    - Endpoints `/v1/queue/{ticket}` (GET & DELETE) priamo v `LeaseEndpoints`.
  - **Klientske SDK Auto-Wait (`Symbolon.Client`)**:
    - `SymbolonClient.AcquireSeatAsync`: Podpora parametrov `allowQueue`, `maxQueueWait`, `priority`, adaptívny polling cyklus s `TimeProvider`, verifikácia tokenu a korektné uvoľnenie/zrušenie lístka pri vypršaní timeoutu.
    - Rozšírenie `SymbolonClientOptions` o `AllowQueue`, `MaxQueueWait`, `Priority`, `UserId`, `MachineId`.
  - **Viacjazyčné SDK (Multi-Language Parity)**:
    - **Python SDK**: `acquire_seat` s parametrami `allow_queue`, `max_queue_wait_seconds`, `priority`, `user_id`, automatickým čakaním vo fronte a zrušením lístka pri timeoute.
  - **CLI Nástroje (`Symbolon.Cli`)**:
    - Príkazy `symbolon queue list`, `symbolon queue status <ticket>`, `symbolon queue cancel <ticket>`, `symbolon queue promote <ticket>`.
  - **Web Dashboard & Retro FoxPro TUI**:
    - Obrazovka `view-queue` s KPI kartami (čakajúci, priemerná priorita, odhadovaný čas čakania), tabuľkou lístkov a akciami na manuálnu propagáciu a zrušenie lístkov.
    - Retro FoxPro TUI integrácia s klávesovou skratkou `Q` a hash navigáciou `#queue`.
  - **Testovacie Pokrytie**:
    - 344 .NET unit/integration testov naprieč všetkými 11 projektmi (100% pass rate, 0 varovaní, 0 chýb s `TreatWarningsAsErrors=true`), 17 Python SDK testov.

- **Enterprise Audit Engine, True-Up Compliance Reporting & Audit-Chain Concurrency Analytics (FLT-37, FLT-38, FLT-39, Goal G5, §6.5 Enterprise Procurement)**:
  - **Doménová a analytická vrstva (`Symbolon.Domain.Reporting`)**:
    - `AuditPeakConcurrencyCalculator`: Deterministická schodisková funkcia špičkovej súbežnosti počítaná priamo z nemenných auditných udalostí (`SeatCheckoutCompleted`, `SeatReleased`, `HeartbeatRenewed`, `SeatAcquisitionDenied`, `FeatureAcquired`, `FeatureReleased`) podľa normatívneho pravidla **FLT-38** bez akejkoľvek aproximácie či vzorkovania. Poskytuje presné diskrétne časové vedrá (`hour`, `day`, `week`, `month`) s výpočtom špičky (peak), časovo váženého priemeru (time-weighted average), počtu checkoutov a zamietnutí.
    - `TrueUpReportGenerator`: Enterprise True-Up audit výkaz pre oddelenie nákupu (Procurement) podľa cieľa **G5** a kapitoly §6.5. Vyhodnocuje zmluvný stav licencie (`Compliant`, `OverageWarning`, `NonCompliant`), presné množstvo nadlimitných sedadiel a spoplatniteľné sedadlosekundy (billable seat-seconds) s natívnym exportom do formátu CSV a JSON.
    - `AuditChainIntegrityVerifier`: Matematická kryptografická verifikácia append-only reťazca SHA-256 hashov podľa normatívneho pravidla **FLT-37**. Zabezpečuje fixed-time porovnávanie hashov, detekciu manipulácie, vymazania alebo spätnej úpravy auditných záznamov s kanonickou milisekundovou normalizáciou časových pečiatok.
  - **Protokol a AOT serializácia (`Symbolon.Protocol.Reporting`)**:
    - DTO kontrakty: `ConcurrencyTimeBucketDto`, `ConcurrencyAnalyticsResponseDto`, `TrueUpLicenseSummaryDto`, `TrueUpReportDto`, `DenialRecordDto`, `DenialsAnalyticsResponseDto`, `AuditVerificationProofDto`.
    - Plná registrácia v `SymbolonProtocolJsonContext` pre nulovú alokáciu a natívnu AOT kompiláciu.
  - **REST API (`Symbolon.ControlPlane`)**:
    - `GET /admin/v1/reports/concurrency` (so spätnou kompatibilitou) a `GET /admin/v1/reports/concurrency/timeline`
    - `GET /admin/v1/reports/true-up`
    - `GET /admin/v1/reports/true-up/export?format=csv|json`
    - `GET /admin/v1/reports/denials` (FLT-39 analytika zamietnutí a úzkych hrdiel)
    - `POST /admin/v1/reports/audit/verify-integrity` (FLT-37 kryptografická kontrola)
  - **CLI Nástroje (`symbolon reports`)**:
    - Príkazy `symbolon reports concurrency`, `symbolon reports true-up`, `symbolon reports denials` a `symbolon reports verify-audit` s ANSI minigrafmi vyťaženia (`RenderMiniBar`), farebnými stavmi súladu a podporou parametrov `--server`, `--license`, `--from`, `--to`, `--bucket`, `--export`.
  - **Web Dashboard & Retro FoxPro TUI**:
    - Obrazovka `view-reports` s dynamickým filtrom (licencia, časové vedro, časové rozpätie), 4 KPI kartami, časovou osou súbežnosti, prehľadom True-Up prekročení a interaktívnym spúšťaním verifikácie hashov.
    - Retro FoxPro TUI navigácia s klávesovou skratkou `F12` / `Reporty`, akcelerátorom `y` a integráciou kaskádového menu.
  - **Testovacie Pokrytie**:
    - 334 .NET unit/integration testov naprieč všetkými 10 projektmi (100% pass rate, 0 varovaní, 0 chýb s `TreatWarningsAsErrors=true`).

- **Zero-Config Server Discovery, Resilient Multi-Server Failover Pool & Enterprise Environment Variable Resolution (UDP Multicast / Broadcast, SYMBOLON_LICENSE_SERVER, SDK Auto-Discovery & CLI)**:
  - **Protokolová vrstva (`Symbolon.Protocol`)**:
    - Štandardný UDP discovery port `7584` (`0x1D90`) podľa špecifikácie.
    - DTO kontrakty `DiscoveryProbePacket`, `DiscoveryAnnouncementPacket` a `DiscoveredServerInfo` zaregistrované v AOT JSON source generátore `SymbolonProtocolJsonContext`.
  - **Client & Failover Pool (`Symbolon.Client`)**:
    - `SymbolonServerResolver`: Enterprise parsovač reťazcov serverov podporujúci FlexNet `port@host` a `@host`, štandardné `http`/`https` URI, zoznamy oddelené bodkočiarkami a čiarkami, a automatický fallback na premenné `SYMBOLON_LICENSE_SERVER` a `SYMBOLON_SERVERS`.
    - `ServerFailoverPool`: Robustný pool serverov s priebežným vyhodnocovaním zdravia uzlov, evidenciou zlyhaní, cooldown lehotami a transparentným prepnutím (failover) pri sieťových chybách a HTTP 502/503/504 stavoch.
    - `SymbolonDiscoveryClient`: Odosielanie UDP broadcast/multicast sond a meranie RTT sieťovej latencie aktívnych serverov.
    - Integrácia failover poolu priamo do `SymbolonClient.AcquireSeatAsync`, `BorrowSeatAsync` a `ReturnBorrowedSeatAsync`.
  - **Serverové Responders (`Symbolon.ControlPlane` & `Symbolon.Relay`)**:
    - `DiscoveryResponderService` pre `ControlPlane` odpovedajúci na prichádzajúce UDP sondy identitou klastra a URL servera.
    - `RelayDiscoveryResponderService` pre `Relay` oznamujúci dostupnosť lokálneho edge relay uzla.
  - **Viacjazyčné SDK (Multi-Language Parity)**:
    - **Python SDK**: Funkcie `resolve_license_servers()`, `discover_servers()` a automatické rozlíšenie servera pri inicializácii `SymbolonClient(product_code=...)`.
    - **Rust SDK**: Funkcia `resolve_license_servers()` a konštruktor `SymbolonClient::from_env(product_code)`.
    - **C / C++ SDK**: Funkcia `symbolon_resolve_server()` pre rozlíšenie FlexNet a URL reťazcov z premenných prostredia.
  - **CLI Nástroje (`Symbolon.Cli`)**:
    - Nové príkazy `symbolon discover` (live UDP broadcast sonda do lokálnej siete so Spectre.Console tabuľkou a RTT latenciou) a `symbolon servers resolve` pre interaktívne testovanie enterprise syntaxe.
  - **Testovacie Pokrytie**:
    - 305 .NET unit/integration testov naprieč všetkými 10 projektmi (100% pass rate, 0 varovaní, 0 chýb s `TreatWarningsAsErrors=true`), 15 Python SDK testov, 12 Wasm validator testov (celkovo 332 automatizovaných testov).

- **Dynamic Entitlements & Granular Feature Flagging Engine (Tiered Modules, Package Suites, Per-Feature Concurrency Limits, SDK RAII Context Managers, Web UI, CLI & Multi-Language SDK Integration)**:
  - **Dátová a doménová vrstva (`FeatureDefinitionEntity`, `PackageSuiteEntity`, `LicenseEntitlementEntity`, `ActiveFeatureLeaseEntity`)**:
    - Nezávislé kvóty sedadiel pre jednotlivé funkčné moduly a add-ony oddelené od kapacity hlavnej licencie podľa vzoru FlexNet/RLM/Sentinel RMS.
    - Balíkové suity (Package Suites) s automatickou dekompozíciou a expanziou na dcérske moduly pri chekoute.
    - Deterministický `VersionRangeMatcher` podporujúci verzie `*`, presnú zhodu, divokú kartu (napr. `2026.*`) a intervalové porovnávanie (`>= 2025.0 and <= 2027.0`).
    - Atómové prideľovanie a uvoľňovanie sedadiel modulov v `EfFeatureEntitlementStore` s garanciou kapacity a automatickým cleanupom expirovaných prenájmov.
  - **Protokol a REST API**:
    - `POST /v1/leases/{id}/features/acquire` - Dynamická alokácia modulu za behu s kontrolou kvóty a kompatibility verzie bez nutnosti opätovného checkoutu celej aplikácie.
    - `POST /v1/leases/{id}/features/release` - Explicitné uvoľnenie modulu späť do zdieľaného fondu.
    - `GET /v1/leases/{id}/features` - Zoznam aktuálne držaných dynamických modulov pre daný lease.
    - Kaskádové uvoľnenie všetkých modulov pri uvoľnení hlavného sedadla (`DELETE /v1/leases/{id}`).
    - Plnohodnotná administrátorská správa na `/admin/v1/entitlements/features`, `/suites`, `/licenses/{id}`, `/usage`.
  - **Multi-Language Client SDK Integrácie**:
    - **C# SDK (`Symbolon.Client`)**: `FeatureLease` (`IAsyncDisposable`, `IDisposable`), `lease.HasFeature()`, `await using var feat = await lease.UseFeatureAsync("FEA_SOLVER")`, `lease.AcquireFeatureAsync()`, `lease.ReleaseFeatureAsync()`, `SymbolonFeatureDeniedException`.
    - **Python SDK (`symbolon`)**: `FeatureLease` context manager, `lease.has_feature()`, `with lease.use_feature("FEA_SOLVER"):`, `lease.acquire_feature()`, `lease.release_feature()`, výnimka `FeatureDenied`.
    - **Rust SDK (`symbolon-client`)**: RAII štruktúra `FeatureLease<'a>` s implementáciou `Drop` pre automatické uvoľnenie pri zániku scope, `lease.has_feature()`, `lease.acquire_feature()`, `lease.use_feature(...)` uzáverový helper, chybové stavy `FeatureDenied` a `FeatureCapacityExceeded`.
    - **C / C++ SDK (`symbolon.h`)**: ANSI C API `symbolon_lease_has_feature`, `symbolon_acquire_feature`, `symbolon_release_feature`, `symbolon_feature_lease_get_code`, moderná C++17 RAII trieda `ScopedFeatureLease`.
    - **Wasm SDK (`symbolon-validator.js`)**: Funkcia `checkFeatureEntitlement(claims, featureCode, requestedVersion)` pre offline validáciu modulov a suít.
  - **CLI Nástroje (`symbolon features`)**:
    - Príkazy `symbolon features list`, `create`, `delete`, `suites`, `create-suite`, `delete-suite`, `grant`, `usage` so Spectre.Console tabuľkami a live grafickými teplomermi vyťaženia.
  - **Web Dashboard & Retro FoxPro TUI**:
    - Moderný dashboard pohľad `view-features` so zobrazením live concurrency gauge meračov vyťaženia, katalógom modulov a balíkových suít a interaktívnymi modálmi pre vytvorenie.
    - Retro FoxPro TUI ponuka `F[u]nkcie & Moduly` (klávesová skratka `U`) a katalóg modulov a suít v `Číselníkoch`.
  - **Testovacie Pokrytie**:
    - 298 .NET unit/integration testov naprieč všetkými 10 projektmi (100% pass rate), 14 Python SDK testov, 12 Wasm validator testov (celkovo 324 automatizovaných testov).

- **Enterprise Webhook & Event Notification Engine (HMAC-SHA256, Slack/Teams Integration, Dead-Letter Queue & Automated License Expiration Lifecycle)**:
  - **Kryptografické HMAC-SHA256 Podpisy & Anti-Replay Ochrana (`WebhookSecurity`)**:
    - Generovanie štandardizovanej podpisovej hlavičky `X-Symbolon-Signature: t={timestamp},v1={hex_signature}`.
    - Ochrana proti útokom typu replay s validáciou časovej odchýlky (time drift tolerance default 5 minút).
    - Bezpečné overovanie podpisov v konštantnom čase pomocou `CryptographicOperations.FixedTimeEquals`.
  - **Formátovacie Adaptéry pre Slack & Microsoft Teams (`WebhookAdapters`)**:
    - Natívne formátovanie pre Standard JSON (`SymbolonWebhookPayload`), Slack Incoming Webhooks (farebné bloky podľa závažnosti udalosti: červená pre odmietnutia a podvody `#E01E5A`, jantárová pre expirácie a nízky stav tokenov `#ECB22E`, zelená pre vydania a vrátenia `#2EB67D`), a Microsoft Teams Connector (Office 365 Connector MessageCards).
  - **Asynchrónna Bounded Fronta & Background Dispatcher (`IWebhookQueue`, `WebhookBackgroundService`)**:
    - Pamäťovo ohraničený `Channel<WebhookEvent>` pre nulovú latenciu licenčných operácií.
    - Background worker streamujúci udalosti s perzistenciou do databázy cez `EfWebhookStore`, meraním dĺžky volania `DurationMs`, inkrementáciou chýb a označovaním zlyhaných doručení do Dead-Letter Queue (DLQ).
  - **Dead-Letter Queue (DLQ) & Manuálny Replay Endpoint**:
    - Automatické presunutie do stavu `dead_letter` po 3 neúspešných pokusoch.
    - Endpoint `POST /admin/v1/webhooks/deliveries/{id}/replay` pre okamžité zopakovanie zlyhanej správy.
    - Filtrovanie doručení podľa stavu cez `GET /admin/v1/webhooks/deliveries?status=delivered|failed|dead_letter`.
  - **Engine pre Automatizáciu Životného Cyklu Licencií (`LicenseLifecycleEngine`)**:
    - Vyhodnocovanie stavu licencií: `Perpetual`, `ActiveNormal`, `ExpiringSoon` (konfigurovateľný horizont, default 14 dní), `SoftGrace` (ochranná lehota so zachovaným prístupom), `HardGrace`, `Expired`.
    - Generovanie a odosielanie lifecycle eventov: `license.expiring_soon`, `license.grace_entered`, `license.expired`.
    - Endpointy `GET /admin/v1/lifecycle/expiring` a `POST /admin/v1/lifecycle/evaluate`.
  - **Prepojenie Licenčných Udalostí (Event Hooking)**:
    - Napojenie na zamietnutie sedadiel (`lease.denied` a spätne kompatibilné `seat.denied`), detekciu anomálií a klonovania (`fraud.detected`), a nízke stavy kreditových peňaženiek (`token.threshold_low`).
  - **CLI Príkazy pre Webhooky a Životný Cyklus (`Symbolon.Cli.Commands.WebhookCommands`)**:
    - `symbolon webhooks list`, `create`, `delete`, `test`, `deliveries`, `replay`, `lifecycle`.
  - **Web Dashboard & Retro FoxPro TUI Rozšírenie**:
    - Aktualizovaný panel `view-webhooks` s KPI kartami (Aktívne, Doručené, Zlyhania, DLQ), filtrom histórie doručení a tlačidlom pre manuálnu kontrolu expirácií.
    - Nový modálny dialóg s podporou pre zadanie názvu, výber formátu (JSON / Slack / Teams) a rozšírených typov udalostí.
    - Retro FoxPro TUI navigácia s priamym prístupom cez `W` a klávesovou skratkou `F8`.
  - **Testovacie Pokrytie**:
    - 280 .NET testov (všetkých 10 projektov na 100% pass rate), 13 Python testov, 11 Wasm testov (celkovo 304 testov).
- **Enterprise Migration Engine & Transpiler (FlexNet/FLEXlm, Options.opt, lmgrd Log Analytics & Keygen Importer)**:
  - **FlexNet License File Parser (`FlexNetLicenseParser`)**:
    - Robustné spracovanie súborov `license.dat` / `*.lic` pre servery, daemony a licenčné atribúty (`SERVER`, `DAEMON`/`VENDOR`, `FEATURE`, `INCREMENT`, `PACKAGE`, `UPGRADE`).
    - Plná podpora zalomenia riadkov pomocou spätných lomiek `\`, normalizácia formátov dátumov (`dd-MMM-yyyy`, `permanent`, `0`, `none`), neobmedzených (`uncounted`) aj diskrétnych sedadiel.
    - Extrakcia `HOSTID` väzieb (MAC adresy, IP adresy, hostname) a balíkových komponentov.
    - Automatizovaná konverzia do natívnych Symbolon produktových plánov s bezpečnostnými a architektonickými odporúčaniami (náhrada krehkých triadov za PostgreSQL HA + Delegated Seat Grants).
  - **FlexNet Options.opt Transpiler (`FlexNetOptionsTranspiler`)**:
    - Deterministický transpilér politík zo súborov `options.opt` do natívnych JSON pravidiel Symbolon (`PolicyModel`).
    - Konverzia pravidiel: `GROUP`, `HOST_GROUP`, `RESERVE`, `MAX`, `INCLUDE`/`INCLUDEALL`, `EXCLUDE`/`EXCLUDEALL`, `BORROW_LOWWATER`, `MAX_BORROW_HOURS`, `TIMEOUT`/`TIMEOUTALL`, `LINGER`, `REPORTLOG`.
    - Generovanie prehľadného transpilátorského reportu (`OptionsTranspilationReport`) s mapovaním pravidiel a detekciou nekompatibilít.
  - **FlexNet lmgrd Log Analyzer & Right-Sizing Engine (`FlexNetLogAnalyzer`)**:
    - Analytické spracovanie servisných protokolov `lmgrd.log` (`OUT`, `IN`, `DENIED`, `UNSUPPORTED`).
    - Výpočet časovej krivky súbehu (concurrency timeline), historického maxima súbežných sedadiel (peak concurrent seats), celkového objemu výpožičiek a vrátení, miery zamietnutí licencií (denials) a unikátnych používateľov a staníc.
    - 24-hodinový rozpad vyťaženia sedadiel pre identifikáciu špičiek.
    - **Right-Sizing algoritmus**: Výpočet optimálnej kapacity sedadiel a odporúčaného kreditového prečerpania (overdraft buffer) zabraňujúci zbytočnému preplácaniu licencií.
  - **Keygen.sh Cloud API Importer (`KeygenImporter`)**:
    - Import exportovaných JSON dát z platformy Keygen.sh (`policies`, `licenses`, `users`).
    - Automatické mapovanie modelov atribútov, floating a node-locked licencií do Symbolon schémy.
  - **REST API Endpointy pre Migráciu (`MigrationEndpoints`)**:
    - `POST /admin/v1/migrate/flexnet/license`: Analýza alebo priama aplikácia (`apply=true`) licenčných súborov FlexNet.
    - `POST /admin/v1/migrate/flexnet/options`: Transpilácia pravidiel `options.opt`.
    - `POST /admin/v1/migrate/flexnet/log-analysis`: Spracovanie logov `lmgrd.log` s kalkuláciou optimálneho počtu sedadiel.
    - `POST /admin/v1/migrate/keygen`: Import a materializácia entít z Keygen.sh do databázy `SymbolonDbContext`.
  - **CLI Príkazy pre Migráciu (`Symbolon.Cli.Commands.MigrateCommands`)**:
    - `symbolon migrate flexnet [--file <path>] [--apply] [--out <file>]`: Konverzia licenčného súboru.
    - `symbolon migrate options [--file <path>] [--out <file>]`: Transpilácia options pravidiel.
    - `symbolon migrate log [--file <path>] [--out <file>]`: Log analytika a right-sizing odporúčania.
    - `symbolon migrate keygen [--file <path>] [--apply] [--out <file>]`: Import z Keygen.sh.
  - **Web Dashboard & Retro FoxPro TUI Integrácia**:
    - Nové zobrazenie `view-migrate` (🔄 Migrácia z FlexNet / Keygen) s tromi interaktívnymi panelmi: Transpilér licencií, Log Analytik s Right-Sizing KPI a Keygen Importer vrátane vzorových šablón na jedno kliknutie.
    - Rozšírenie Retro FoxPro TUI o položku `Migrácia (I)` a klávesovú skratku `I`.
  - **Testovacie Pokrytie**:
    - 9 nových unit a integračných testov v `MigrationTests`, `MigrationApiTests` a `CliCommandsTests` (celkovo 256 .NET testov, 280 testov v celej platforme, 100% pass rate).
- **Token & Credit-Based Metered Licensing Engine (Pay-As-You-Go, Peňaženky & Auditný Ledger)**:
  - **Dátová Vrstva a Entitné Modely (`Symbolon.Data`)**:
    - Entita `TokenWalletEntity`: kreditový fond/peňaženka viazaná na tenanta a voliteľne licenciu s evidenciou `TotalCredits`, `Balance`, `ReservedCredits`, `OverdraftLimit`, `ExpiresAt`, `ThresholdLowAlert`.
    - Entita `TokenRateEntity`: tarifikácia funkcií viazaná na tenanta a produkt s konfiguráciou sadzby za minútu (`RatePerMinute`) alebo za jednotku/job (`RatePerUnit`).
    - Entita `TokenReservationEntity`: stavové rezervácie (2PC: `pending`, `committed`, `rolled_back`, `expired`) s TTL a časom expirácie.
    - Entita `TokenLedgerEntryEntity`: nemenný auditný záznam každej kreditovej transakcie (`credit`, `reserve`, `consume`, `release`, `refund`) s idempotenciou.
    - Konfigurácia EF Core v `SymbolonDbContext` s explicitnou decimálnou presnosťou `HasPrecision(18, 4)` a indexmi pre rýchle vyhľadávanie.
  - **Doménový Motor (`Symbolon.Domain.Tokens`)**:
    - Kontrakty `ITokenStore` a `ITokenEngine` s kompletnou doménovou implementáciou v `TokenEngine`.
    - Implementácia perzistencie v `EfTokenStore` s atomickými operáciami aktualizácie zostatkov.
    - Dvojfázové rezervovanie kreditov: rezervácia pred výpočtom s kalkuláciou sadzby, predlžovanie rezervácie cez heartbeat a finálne zúčtovanie / vrátenie nepoužitých kreditov.
    - Riadenie povoleného prečerpania (`OverdraftLimit`) a detekcia poklesu pod varovnú hladinu (`ThresholdLowAlert`).
  - **REST API Endpointy (`Symbolon.ControlPlane.Endpoints.TokenEndpoints`)**:
    - Klientske metered endpointy: `POST /v1/tokens/reserve`, `POST /v1/tokens/heartbeat`, `POST /v1/tokens/commit`, `POST /v1/tokens/rollback`, `GET /v1/tokens/wallets/{id}/balance`.
    - Správcovské admin endpointy: `GET/POST /admin/v1/tokens/wallets`, `POST /admin/v1/tokens/wallets/{id}/credit`, `GET/POST /admin/v1/tokens/rates`, `GET /admin/v1/tokens/ledger`.
  - **CLI Podpora (`Symbolon.Cli.Commands.TokenCommands`)**:
    - Príkazy `symbolon tokens wallets`, `symbolon tokens create`, `symbolon tokens credit`, `symbolon tokens rates`, `symbolon tokens set-rate`, `symbolon tokens balance`.
    - Ochrana pred zlyhaním v neinteraktívnom / CI/CD režime (`Console.IsInputRedirected`).
  - **Web TUI & Retro FoxPro TUI Integrácia**:
    - Nové zobrazenie `view-tokens` (🪙 Tokeny & Kredity) v hlavnom paneli s prehľadovými kartami peňaženiek, sadzobníkov a auditného ledgeru.
    - Modálne okná pre vytvorenie peňaženky, dobitie kreditu a konfiguráciu sadzieb.
    - Rozšírenie Retro FoxPro TUI o položku `Tokeny (E)` a klávesové skratky.
  - **Testovacie Pokrytie**:
    - 16 nových unit a integračných testov v `TokenEngineTests`, `EfTokenStoreTests`, `TokenApiTests` a `CliCommandsTests` (celkovo 247 .NET testov, 271 testov v celej platforme, 100% pass rate).
- **Enterprise Cloud KMS / Hardware HSM Integrácia & 3-Úrovňová Hierarchia Kľúčov (§9.3)**:
  - **Cloud KMS & Hardware HSM Provideri (`Symbolon.Crypto.Kms`)**:
    - Abstraktné rozhranie `IKmsProvider` pre hardvérovú izoláciu kľúčov a kryptografické operácie s asynchrónnym health checkom a enumeráciou kľúčov.
    - Implementácie poskytovateľov: `AzureKeyVaultKmsProvider` (Azure Key Vault REST HSM), `AwsKmsProvider` (AWS KMS Sign API), `MockHardwareHsmProvider` (PKCS#11 HSM emulácia pre testovacie a vývojové prostredia) a `EncryptedEnvelopeKmsProvider`.
  - **AES-256-GCM Šifrovaná Obálka s Kľúčovou Deriváciou PBKDF2 (`EncryptedEnvelopeKeyStore`)**:
    - Ochrana citlivých privátnych kľúčov (PKCS#8) pomocou symetrického šifrovania AES-256-GCM s 12-bajtovým kryptografickým noncom a 16-bajtovým autentifikačným tagom.
    - Odolné odvodzovanie šifrovacieho kľúča pomocou PBKDF2 s 100 000 iteráciami SHA-256 a kryptografickým saltom (32 bajtov).
    - Serializácia a deserializácia do štandardného PEM formátu: `-----BEGIN ENCRYPTED SYMBOLON KEY-----` / `-----END ENCRYPTED SYMBOLON KEY-----`.
  - **3-Úrovňová Hierarchia Kľúčov a Digitálnych Certifikátov (`Symbolon.Crypto.Hierarchy`)**:
    - Striktné oddelenie rolí podľa §9.3 bezpečnostnej špecifikácie:
      - **Tier 1 (Root Master Anchor)**: Offline/Cold kľúč s platnosťou 10 rokov, slúži výhradne na podpisovanie certifikátov intermediate autorít.
      - **Tier 2 (Product Authority)**: Intermediate autorita pre konkrétnu produktovú líniu (platnosť 2 roky), podpisuje efemerálne kľúče.
      - **Tier 3 (Lease Leaf Key)**: Efemerálny kľúč viazaný na uzol klastra alebo sedadlo (platnosť 30 dní).
    - Modely `KeyHierarchyChain`, `SignedKeyCertificate`, `KeyChainVerificationResult` a overovací motor `KeyHierarchyEngine`.
    - Kryptografická verifikácia celého reťazca dôvery vrátane overenia správnosti rolí, časových prekryvov platností a digitálnych podpisov ECDSA.
  - **Production Key Safety Guard (§9.3 Pravidlo 4 Fail-Fast)**:
    - Striktná kontrola pred štartom servera v `ProductionKeySafetyGuard`: odmietnutie nábehu servera v produkcii (`ASPNETCORE_ENVIRONMENT=Production`), pokiaľ sú v konfiguračných súboroch alebo prostredí nájdené nezašifrované privátne kľúče v čistom texte (plaintext).
  - **REST API Endpointy pre KMS a Hierarchiu**:
    - `GET /admin/v1/kms/status`: Diagnostika stavu pripojenia, typu poskytovateľa a evidovaných kľúčov.
    - `GET /admin/v1/keys/hierarchy`: Získanie aktívneho 3-úrovňového reťazca certifikátov.
    - `POST /admin/v1/keys/hierarchy/verify`: Kryptografická verifikácia reťazca certifikátov voči Root kotve.
  - **CLI Príkazy pre KMS a Obálkové Šifrovanie**:
    - `symbolon kms status [--provider <env|azure|aws|pkcs11>]`: Zobrazenie stavu Cloud KMS / HSM.
    - `symbolon keys envelope [--in <file>] [--passphrase <p>] [--out <file>]`: Šifrovanie PKCS#8 privátneho kľúča AES-256-GCM obálkou.
    - `symbolon keys hierarchy`: Vizuálny výpis a kryptografická verifikácia 3-úrovňového stromu kľúčov (Root ➜ Product ➜ Lease).
  - **Web TUI & Retro FoxPro TUI Integrácia**:
    - Nové karty v zobrazení `view-keys` pre Cloud KMS stav a vizualizáciu reťazca dôvery s tlačidlom okamžitého overenia.
    - Rozšírené kaskádové menu Retro FoxPro TUI o skratky `Č ➜ M` (KMS status) a `Č ➜ 3` (3-úrovňová hierarchia).
  - **Testovacie Pokrytie**:
    - 15 nových unit a integračných testov v `EncryptedEnvelopeKeyStoreTests`, `KeyHierarchyTests` a `KmsAndHierarchyApiTests` (celkovo 231 .NET testov a 255 testov celkovo, 100% pass rate, 0 varovaní).
- **WebAssembly & In-Browser Offline License Validator SDK (`@symbolon/validator`)**:
  - **W3C WebCrypto API Kryptografické Overovanie (NIST P-256 / ES256)**:
    - Zero-dependency klientsky validátor bežiaci 100% v prehliadači, Node.js a hybridných prostrediach (Electron, Tauri, React, Vue, Angular).
    - Kryptografická verifikácia digitálnych podpisov ECDSA SHA-256 pomocou `crypto.subtle.verify` priamo z JWKS verejných kľúčov bez volania servera.
    - Okamžitá detekcia a odmietnutie manipulácie s počtom sedadiel (tampered payload), poškodených bajtov podpisu, nesprávneho kľúča alebo expirovanej platnosti.
  - **Hardvérový Odtlačok Prehliadača pre Web Node-Locking (`generateBrowserFingerprint`)**:
    - Deterministický výpočet stabilného SHA-256 hashu (`fp_web_...`) z 2D Canvas renderingu, WebGL rendereru, rozlíšenia obrazovky, časového pásma, jazyka a hardvérovej súbežnosti.
  - **Viacformátová Podpora a Balíčkovanie**:
    - Podpora `.symlic` / `.symkey` PEM obálok, JWS Compact Serialization, JWS JSON General Serialization a `LIC-34` formátu produktových kľúčov (Crockford Base32).
    - Balíčky: UMD `symbolon-validator.js`, ES Module `symbolon-validator.mjs`, TypeScript definície `symbolon-validator.d.ts` a NPM `package.json`.
  - **Interaktívny Validátor v Retro FoxPro Web TUI a Standalone HTML Portál**:
    - Nová sekcia `view-wasm` (**🛡️ Wasm Validátor**) v administračnom rozhraní s okamžitým drag-and-drop overovaním a detekciou odtlačku.
    - Samostatný jedno-súborový offline portál `sdk/wasm/index.html` spustiteľný priamo z lokálneho disku (`file://`) bez webového servera.
  - **Testovacia Sada Node.js a .NET**:
    - 11 nových automatizovaných testov v `sdk/wasm/tests/validator.test.js` spúšťaných cez `node --test` (100% pass rate).
    - Aktualizovaná sada `WebUiTests` v `Symbolon.ControlPlane.Tests` overujúca servovanie statických validačných skriptov.
- **Cloud-Native Kubernetes Operator & Enterprise Helm Chart (`Symbolon.Operator`)**:
  - **Custom Resource Definitions (CRD v1)**:
    - `SymbolonCluster` (`licensing.symbolon.io/v1alpha1`): Deklaratívna definícia topológie klastra, počtu replík, hybridného PQC režimu, PostgreSQL úložiska (embedded/external), Ingressu s cert-managerom, PodDisruptionBudget a ServiceMonitoringu.
    - `SymbolonLicense` (`licensing.symbolon.io/v1alpha1`): Deklaratívny manažment licencií a ich injekcia do Kubernetes tajomstiev (Secrets) s automatickým sledovaním stavu (Active/Expired/Suspended), zostávajúcich dní a alokovaných sedadiel priamo cez `kubectl get symbolonlicenses`.
  - **Reconcilers (`ClusterReconciler`, `LicenseReconciler`) & Generator (`K8sManifestGenerator`)**:
    - Automatizované generovanie a ladenie Kubernetes Deploymentov s bezpečnostným kontextom (NonRoot UID 10001, ReadOnlyRootFilesystem, drop ALL capabilities).
    - Automatické generovanie ClusterIP / LoadBalancer Service, Ingress s TLS certifikátmi a cert-manager anotáciami.
    - Generovanie `PodDisruptionBudget` (PDB) s `minAvailable: 1` garantujúce nulový výpadok pri rolling updates klastra.
    - Generovanie `ServiceMonitor` pre Prometheus Operator (`release: prometheus`) s metrickým endpointom `/metrics`.
    - Generovanie Kubernetes Secretov obsahujúcich surové PEM dáta a certifikáty pre klientske pody.
  - **Enterprise Helm Chart Rozšírenia (`deploy/helm/symbolon`)**:
    - Pridané šablóny `pdb.yaml`, `networkpolicy.yaml` (Zero-Trust izolácia portov 8080/8443) a `servicemonitor.yaml`.
    - Aktualizovaný `values.yaml` s prepínačmi pre PDB, NetworkPolicy a Prometheus Operator ServiceMonitor.
    - Zahrnuté CRD definície v `deploy/helm/symbolon/crds/`.
  - **CLI Rozšírenia pre Kubernetes & GitOps (`symbolon k8s`)**:
    - `symbolon k8s crd`: Export CRD manifestov pre `SymbolonCluster` a `SymbolonLicense` do stdout alebo YAML súborov.
    - `symbolon k8s export-license`: Export existujúceho `.symlic` súboru do formátu CRD `SymbolonLicense`.
    - `symbolon k8s generate-cluster`: Vygenerovanie hotového klientskeho manifestu klastra so zadaným počtom replík a PQC nastavením.
  - **Testovacie Pokrytie**:
    - 8 nových integračných a unit testov v `Symbolon.Operator.Tests` pokrývajúcich generovanie manifestov, PDB, Ingress, Secret a reconcilery (216 .NET testov a 13 Python testov prechádza so 100% úspešnosťou a 0 varovaniami).
- **SAML 2.0 / OIDC Single Sign-On (SSO) pre Web TUI Administráciu s Enterprise RBAC Mapovaním Rolí**:
  - **OIDC Core 1.0 Authorization Code Flow s PKCE (RFC 7636)**:
    - Bezstavový generátor kryptografického PKCE verifikátora a challenge SHA-256 (`S256`).
    - Bezstavová ochrana stavu relácie cez kryptograficky podpísaný HMAC-SHA256 token (`ProtectState` / `UnprotectState`) s 15-minútovou platnosťou chrániacou pred CSRF útokmi bez potreby stavových tabuliek v pamäti.
    - OIDC Endpointy: `GET /auth/sso/oidc/login` (iniciácia a presmerovanie na IdP) a `GET /auth/sso/oidc/callback` (výmena autorizačného kódu za tokeny, extrakcia identitných claims a vydanie relácie).
  - **SAML 2.0 Web Browser SSO Profile (SP-Initiated aj IdP-Initiated)**:
    - Generovanie SP metadát XML (`GET /auth/sso/saml/metadata`) s deskriptorom `SPSSODescriptor` a väzbou `HTTP-POST`.
    - Generovanie požiadaviek `AuthnRequest` s Deflate + Base64 kompresiou (`HTTP-Redirect` binding) cez `GET /auth/sso/saml/login`.
    - Assertion Consumer Service (`POST /auth/sso/saml/acs`) s ochranou proti XXE injekciám (`DtdProcessing.Prohibit`, `XmlResolver = null`), overením stavu `Success`, audience a extrakciou atribútov (NameID, displayName, email, role, groups).
  - **Enterprise RBAC Claim & Skupinový Mapper**:
    - Automatické dynamické mapovanie skupín a rolí z IdP (Okta, Microsoft Entra ID, Keycloak, PingFederate) na interné Symbolon role:
      - `admin:super` (plný prístup ku všetkým entitám a rotácii master podpisových kľúčov)
      - `admin:tenant` (správa licencií, produktov a politík v rámci tenanta)
      - `auditor` (len čítanie logov a prehľadov, blokované modifikačné metódy)
    - Prioritná hierarchia riešenia rolí (`admin:super` > `admin:tenant` > `auditor` > `DefaultRole`).
  - **Duálny Režim Autentifikácie v `ApiKeyAuthenticationHandler`**:
    - Bezproblémové súbežné fungovanie strojových API kľúčov (`X-Api-Key` / `Bearer sym_adm_...`) a podpisovaných SSO relácií (`sym_sso_...` token / zabezpečený HTTP-Only `symbolon_session` cookie).
    - Endpointy pre profil a odhlásenie: `GET /auth/sso/me` a `POST /auth/sso/logout`.
  - **Administrácia Konfigurácií SSO**:
    - REST CRUD endpointy `GET /admin/v1/sso/configs`, `POST /admin/v1/sso/configs`, `DELETE /admin/v1/sso/configs/{id}` s multi-tenantnou izoláciou.
  - **Retro FoxPro Web TUI Rozšírenie**:
    - Topbar odznak prihláseného SSO administrátora `[👤 Meno (Rola)]` s tlačidlom pre odhlásenie `[🚪 Odhlásiť]`.
    - Autentifikačné okno s podporou podnikového SSO a zoznamom aktívnych IdP providerov s priamym prihlásením.
  - **Testovacie Pokrytie**:
    - Nové integračné a unit testy v `SsoApiTests.cs` pokrývajúce OIDC PKCE flow, SAML XML parsovanie a metadáta, RBAC mapovanie a autorizáciu cez cookie reláciu (208 .NET testov a 13 Python testov prechádza so 100% úspešnosťou a 0 varovaniami).
- **Enterprise SCIM 2.0 Identity & Group Synchronization Bridge (`RFC 7643`, `RFC 7644`)**:
  - **Štandardné SCIM 2.0 Endpointy (`/scim/v2`)**:
    - `GET /scim/v2/ServiceProviderConfig`: RFC 7644 §4 schopnosti poskytovateľa služieb (podpora PATCH, filter do 1000 výsledkov, HTTP Bearer autentifikácia).
    - `GET /scim/v2/Schemas` & `GET /scim/v2/ResourceTypes`: Schémy pre entity `User` (`urn:ietf:params:scim:schemas:core:2.0:User`) a `Group` (`urn:ietf:params:scim:schemas:core:2.0:Group`).
    - `Users` CRUD & Filter: `GET /Users` (stránkovanie, filtrovanie `userName eq "..."`, `externalId eq "..."`), `POST /Users` (vytvorenie používateľa s detekciou konfliktov `409 Conflict`), `GET /Users/{id}`, `PUT /Users/{id}`, `PATCH /Users/{id}` a `DELETE /Users/{id}`.
    - `Groups` CRUD & Member Management: `GET /Groups`, `POST /Groups`, `GET /Groups/{id}`, `PUT /Groups/{id}`, `PATCH /Groups/{id}` (pridávanie a odoberanie členov cez `members[value eq "..."]`), a `DELETE /Groups/{id}`.
  - **Zero-Trust Automatické Deprovisioning & Okamžité Uvoľnenie Sedadiel**:
    - Pri deaktivácii používateľa (`active = false` cez SCIM PATCH/PUT) alebo zmazaní (`DELETE /Users/{id}`), server okamžite:
      1. Identifikuje a uvoľní všetky aktívne plávajúce licenčné sedadlá držané používateľom cez `LeaseEngine.ReleaseAsync(leaseId)`.
      2. Zruší všetky čakajúce lístky v kapacitnom rade (`QueueTickets`).
      3. Odoberie menné priradenia z `LicenseUsers` (§4.3).
      4. Zapíše nemenný auditný záznam `SCIM_USER_DEPROVISIONED` do kryptografického ledgeru.
      5. Emituje OpenTelemetry trasovaciu stopu `symbolon.scim.deprovision`.
  - **Kompatibilita s Podnikovými Poskytovateľmi Identity (IdP)**: Plná podpora a interoperabilita s Microsoft Entra ID (Azure AD), Okta, PingFederate a Google Workspace.
  - **Retro FoxPro Web TUI Rozšírenie**:
    - Nová sekcia `view-scim` (**👥 SCIM 2.0 Identity**) v navigačnom paneli s KPI ukazovateľmi (celkový počet synchronizovaných používateľov, aktívne účty, deprovisioned účty).
    - Interaktívna tabuľka podnikového adresára so zoznamom používateľov, externými IdP identifikátormi, skupinami a tlačidlami pre okamžitú deprovisionizáciu/reaktiváciu.
    - Administrátorský endpoint `GET /admin/v1/scim/users`.
  - **Testovacie Pokrytie**: 7 nových komplexných end-to-end integračných testov v `ScimApiTests.cs` pokrývajúcich RFC konfiguráciu, filtrovanie používateľov, skupiny, a overenie okamžitého zrušenia sedadiel pri offboardingu zamestnanca.
- **OpenTelemetry Distribuované Trasovanie & W3C TraceContext (`System.Diagnostics.ActivitySource`)**:
  - **Centrálny ActivitySource (`SymbolonTracing`)**: Globálny zdroj trasovania pre celú platformu (`symbolon.checkout`, `symbolon.renew`, `symbolon.release`, `symbolon.borrow`, `symbolon.return_borrowed`, `symbolon.fraud_check`, `symbolon.replication.sync`, `symbolon.ebpf.enforce`, `symbolon.keys.split`, `symbolon.keys.combine`) s bohatými štandardizovanými tagmi a stavmi chýb/úspechu.
  - **W3C TraceContext Propagácia (`W3cTraceContext`)**: Generovanie, parsovanie a automatická propagácia hlavičiek `traceparent` (`00-{trace_id}-{span_id}-{flags}`) a `tracestate` naprieč .NET Client SDK, Python SDK, Rust SDK, ControlPlane a Relay.
  - **Živý Diagnostický Buffer Stôp (`SymbolonTraceBuffer`) & Admin API**: Kruhový zberateľ stôp pre diagnostiku v reálnom čase s endpointom `GET /admin/v1/traces/recent` (W3C trace ID, span ID, trvanie, stav a tagy).
  - **Retro FoxPro Web TUI Waterfall**: Nová karta a tabuľka v ovládacom paneli (`view-system`) vizualizujúca distribuované stopy, ich latencie v milisekundách a štruktúru atribútov v reálnom čase.
- **Shamir's Secret Sharing ($k$-of-$n$ Prahová Obnova Kľúčov pri Havárii)**:
  - **Konečné Pole Galois Field $GF(2^8)$ (`GaloisField256`)**: Striktná a deterministická aritmetika nad poľom $GF(2^8)$ s ireducibilným polynómom AES/Rijndael $x^8 + x^4 + x^3 + x + 1$ (`0x11B`), log/exp a inverznými tabuľkami pre násobenie a delenie, a Hornerovou schémou vyhodnocovania polynómov.
  - **Prahové Delenie a Lagrangeova Rekonštrukcia (`ShamirSecretSharing`)**: Delenie master podpisových kľúčov (ES256, ML-DSA-65) do $n$ podielov s prahom $k$ ($2 \le k \le n \le 255$). Rekonštrukcia pomocou Lagrangeových bázových polynómov v bode $x = 0$ s overením integrity cez kryptografický kontrolný súčet SHA-256 MAC.
  - **Token & PEM Serializácia Podielov (`SecretShare`)**: Kompaktný tokenový formát `SYMBOLON-SHARE-v1-{k}-{n}-{x}-{Base64Url}` vhodný do príkazového riadka a štandardný PEM formát `-----BEGIN SYMBOLON SECRET SHARE-----`.
  - **CLI Príkazy `symbolon keys split` & `combine`**: `symbolon keys split --in <key> -k <prah> -n <podielov> [--out-dir <dir>]` a `symbolon keys combine --shares <s1,s2,...> [--out <file>]`.
- **License Borrowing & Roaming (Offline Výpožičky pre Poľné Zariadenia)**:
  - **.NET Client SDK (`SymbolonClient`, `SeatLease`)**: Metódy `BorrowSeatAsync(leaseId, days)` a `ReturnBorrowedSeatAsync(leaseId)`. Stav `SeatState.Borrowed`, automatické pozastavenie heartbeat slučky počas trvania roaming výpožičky (1–30 dní) a zachovanie licencie pri ukončení aplikácie.
  - **Python SDK (`SymbolonClient`, `SeatLease`)**: Implementované funkcie `borrow_seat(lease, days)` a `return_borrowed_seat(lease_id)` s bezpečným context managerom zabraňujúcim uvoľneniu pri offline roaming stave.
  - **CLI Rozšírenie (`Symbolon.Cli`)**: Príkazy `symbolon license borrow --lease <id> --days <n>` a `symbolon license return --lease <id>`.
  - **Administrátorský Manažment Výpožičiek**: Endpointy `GET /admin/v1/leases/borrowed` a `POST /admin/v1/leases/{id}/return` na sledovanie a manuálne predčasné vrátenie sedadiel.
  - **Retro TUI Správa Výpožičiek**: Nová interaktívna karta v ovládacom paneli zobrazujúca aktívne offline licencie, zostávajúci čas do expirácie a možnosť okamžitého uvoľnenia.
- **Anti-Fraud & Impossible Travel Velocity / VM Cloning Detection (`IFraudDetectionService`)**:
  - **Impossible Travel Velocity Check**: Výpočet rýchlosti presunu ($v = \Delta d / \Delta t$) pomocou Haversine vzorca na sfére Zeme s detekciou anomálií prekračujúcich rýchlosť dopravných lietadiel ($> 900 \text{ km/h}$) na vzdialenosti $> 100 \text{ km}$.
  - **Detekcia Klonovania VM Snapshotov**: Monitorovanie a okamžitá detekcia duplikovaného hardvérového fingerprintu (SMBIOS UUID, MAC) pristupujúceho z rôznych verejných IP podsietí v rámci aktívneho lease okna.
  - **Automatické Bezpečnostné Alerty (`IAlertService`)**: Automatické generovanie výstrah `impossible_travel` a `vm_cloning`, záznam do kryptografického audit ledgeru a notifikácia cez webhooky.
  - **Retro TUI Bezpečnostný Radar**: Nová monitorovacia karta v reálnom čase vizualizujúca zachytené incidenty, vypočítanú rýchlosť, vzdialenosť a podozrivé IP adresy.
- **Multi-Region Geo-Replication (Active-Active CRDT)**:
  - **Kauzálne Vektorové Hodiny (`VectorClock`)**: Deterministické rozlišovanie kauzálnych a súbežných zmien naprieč geografickými regiónmi (`eu-central-1`, `us-east-1`, `ap-southeast-1`).
  - **Stavový PN-Counter CRDT (`PnCounter`)**: Komutatívne, asociatívne a idempotentné zlučovanie globálnych kapacít a alokovaných sedadiel bez potreby centrálneho distribuovaného zámku.
  - **Disjoint Seat Allocation & LWW-OR-Set (`GeoReplicationEngine`)**: Autonómne bezkolízne prideľovanie sedadiel v každom regióne aj počas úplného sieťového rozpadu (network partition) s automatickým uzdravením (partition healing).
  - **Kryptografická Medziklastrová Autentifikácia (`ReplicationSecurity`)**: Overovanie replikačných správ pomocou HMAC-SHA256 s ochranou proti replay útokom cez časové okno (anti-replay window).
  - **HTTP Replikácia API (`ReplicationEndpoints`)**: Endpointy `POST /v1/replication/sync`, `GET /v1/replication/status` a `POST /v1/replication/peers` pre výmenu deliet.
  - **CLI Príkaz `symbolon cluster`**: `symbolon cluster status` pre zobrazenie topológie, vektorových hodín a CRDT kapacít; `symbolon cluster sync` pre manuálny on-demand delta sync.
  - **Retro Web TUI Integrácia**: Nová vizualizačná karta Active-Active Geo-Replikácie v zobrazení `view-mesh` zobrazujúca vektorový čas a stav jednotlivých regiónov v reálnom čase.
- **eBPF Kernel Socket Enforcement (Linux)**:
  - **Linux BPF Program (`ebpf/symbolon_sock_filter.bpf.c`)**: Nízkoúrovňový BPF CO-RE C program pripájaný k `cgroup/connect4` a `cgroup/connect6` pre striktnú kontrolu socketových spojení priamo na úrovni Linux jadra.
  - **BPF Mapy & Ring Buffer Udalostí (`license_map`, `events_ringbuf`)**: Kernel hash mapa pre overovanie aktívnych leasingov a ring buffer pre asynchrónny zber bezpečnostných incidentov.
  - **Okamžité Blokovanie Neoprávnených Spojení (`-EPERM`)**: Deterministické zablokovanie sieťovej prevádzky aplikácií a procesov bez platnej licencie alebo po okamžitej revokácii.
  - **Jadrový Engine a Emulátor (`EbpfEnforcementEngine`)**: Správa životného cyklu cgroups, portových filtrov a deterministická simulácia pre cross-platform testovanie (Linux, Windows, macOS).
  - **HTTP Endpoints (`EbpfEndpoints`)**: Endpointy `GET /v1/system/ebpf/status`, `GET /v1/system/ebpf/violations`, `POST /v1/system/ebpf/attach` a `detach`.
  - **CLI Nástroje `symbolon ebpf`**: Príkazy `symbolon ebpf status`, `symbolon ebpf attach` a `symbolon ebpf violations`.
  - **Retro TUI Web Dashboard**: Nová monitorovacia karta v `view-mesh` zobrazujúca režim ovládača, chránené cgroups, aktívne BPF položky a zablokované spojenia v reálnom čase.
- **Cross-Platform Release Matrix & GitHub Actions CI/CD**:
  - **Multi-Arch Single-File Binárky**: Zostavovanie samostatných balíčkov pre 6 cieľových platforiem: `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64` (ControlPlane, Relay, CLI).
  - **Automatizované GitHub Actions Release Workflow (`release.yml`)**: Publikácia archívov (`.tar.gz` a `.zip`), agregácia kryptografických kontrolných súčtov `SHA256SUMS.txt`, CycloneDX v1.6 SBOM a automatické vytvorenie GitHub Release pri vytvorení tagu `v*.*.*`.
  - **Rozšírenie Testovacej Matice (`ci.yml`)**: Doplnenie `macos-latest` do CI behu pre garantovanie multi-platformovej stability na Linuxe, Windows a macOS.
  - **Lokálny Zostavovací Skript (`scripts/package-release.ps1`)**: Nástroj na lokálne balenie a overovanie kontrolných súčtov bez nutnosti spúšťať vzdialený pipeline.
- **Multi-Language SDK Attestation Extensions**:
  - **Python SDK (`sdk/python/symbolon/attestation.py`)**: Modul pre hardvérové Enclave / TPM 2.0 PCR citácie (`HardwareAttestationQuote`, `TpmQuoteGenerator`, `TpmQuoteVerifier`), anti-replay nonce verifikáciu a validáciu integrity Secure Boot registrov (PCR 0, 1, 7).
  - **Rust SDK (`sdk/rust/symbolon/src/attestation.rs`)**: Pamäťovo bezpečný modul pre generovanie a konštantno-časovú verifikáciu TPM 2.0 citácií (`HardwareAttestationQuote`, `TpmQuoteGenerator`, `TpmQuoteVerifier`, `EnclaveType`).
  - **Klientske Testy**: 8 automatizovaných unit testov v Python SDK s 100% úspešnosťou.

## [1.0.0] - 2026-09-22

Prvé oficiálne vydanie produkčnej platformy Symbolon — podnikový open-source licenčný server s podporou vysoko-dostupných plávajúcich (floating) licencií, post-kvantovej kryptografie (PQC), on-premise relay uzlov, air-gap sietí a Zero-Trust klastrov.

### Pridané (Added)

#### Kryptografické Jadro & Špecifikácia Formátov (`Symbolon.Crypto`, `Symbolon.Format`)
- **Hybridné Podpisovanie (PQC-Ready)**: Duálne nezávislé podpisovanie kombinujúce klasickú eliptickú kryptografiu **ECDSA P-256 (ES256)** podľa NIST FIPS 186-5 a post-kvantovú schému **ML-DSA-65 (FIPS 204)** chrániacu pred hrozbami kvantových počítačov.
- **Formát Licencií `symlic/1`**: Štruktúrovaný formát licenčných dokumentov založený na JWS General JSON Serialization (RFC 7515) a JWKS (RFC 9964) zabalený v čitateľnej ASCII PEM obálke.
- **Crockford Base32 Licenčné Kľúče**: Generovanie a validácia kľúčov v tvare `SYM-XXXX-XXXX-XXXX-XXXX-XXXX` s integrovaným kontrolným súčtom **CRC-32C (Castagnoli)**.
- **Správa Kľúčov (`SymbolonKeyRing`)**: Bezpečný, vláknovo bezpečný správca podpisových kľúčov s podporou okamžitej revokácie a overovania integrity.
- **WebAssembly (WASM) Offline Validátor**: Ľahké overovacie jadro (`WasmLicenseValidator`) kompilovateľné do WASM pre offline overovanie licencií v prehliadačoch a Node.js bez kontaktu so serverom.
- **OCI Cryptographic License Bundles**: Balenie licencií a JWKS kľúčov do štandardizovaného formátu OCI Image Manifest v1 (`application/vnd.oci.image.manifest.v1+json`) pre air-gapped Kubernetes klastre a privátne kontajnerové registre (`OciLicenseBundle`).
- **Hardware Enclave Attestation (R6)**: Podpora overovania hardvérových citácií TPM 2.0 PCR (Platform Configuration Register) a Confidential Computing Enclaves (Intel SGX / AMD SEV-SNP) chránená challenge-response mechanizmom proti replay útokom (`TpmQuoteVerifier`, `TpmQuoteGenerator`).

#### Distribuovaný Konsenzus & Doménová Logika (`Symbolon.Domain`, `Symbolon.Data`, `Symbolon.Protocol`)
- **Materializované Sedadlá v O(1)**: Deterministické prideľovanie a uvoľňovanie plávajúcich sedadiel pomocou `FOR UPDATE SKIP LOCKED` v PostgreSQL 17 a SQLite bez potreby distribuovaných zámkov (Redis/Raft).
- **Kryptograficky Reťazený Auditný Denník**: Nemenný audit ledger (`IAuditLedger`, `EfAuditLedger`) zreťazený cez SHA-256 (`SHA256(prev_hash + data)`).
- **Menovaní Používatelia & Rezervácie**: Podpora menovaných používateľov a skupín podľa vzoru FlexNet Options (`INCLUDE`, `EXCLUDE`, `GROUP`, `RESERVE`).
- **Metered Kvóty**: Sledovanie a odčítavanie spotreby kreditových jednotiek (entitlement quotas) v reálnom čase.
- **Prioritný Rad Požiadaviek**: FIFO rad s voliteľnou prioritizáciou pri vyčerpaní sedadiel a automatickým checkoutom po uvoľnení kapacity.
- **gRPC / Protobuf Streaming Heartbeat**: Obojsmerný streamovací kanál (`symbolon_lease.proto`, `LeaseStream.cs`) znižujúci réžiu siete o 85 % oproti bežnému HTTP/1.1 REST protokolu.
- **Decentralizovaný Relay Mesh Koordinátor**: P2P synchronizácia delegovaných kapacít medzi autonómnymi Relay uzlami pomocou **Lamportových logických hodín** a disjunktných intervalov sedadiel (`RelayMeshCoordinator`).
- **Air-Gap Delegovaný Portál**: Výmena offline licenčných súborov (`.symreq` → `.symgrant`) cez USB prenos s povinným overením hash-chain uzla `usageDigest`.
- **Hardvérový Fingerprint**: Deterministický zber a kanonický hash hardvérových komponentov (CPU, MAC, SMBIOS, OS).

#### Viacjazyčné Klientske SDK (`sdk/`)
- **.NET 10 LTS SDK (`Symbolon.Client`)**: Plnohodnotný asynchrónny klient s automatickým vláknom pre heartbeat obnovu, adaptívnym jitterom ±10 %, automatickým retry s exponenciálnym backoffom a elegantným RAII uvoľnením (`IAsyncDisposable`).
- **Python SDK (`sdk/python/symbolon`)**: Python knižnica s kontextovým manažérom (`with client.acquire_seat(...)`), automatickým vláknom obnovy a overovaním JWK podpisov.
- **Rust SDK (`sdk/rust/symbolon-rs`)**: Pamäťovo bezpečný klient bez dynamických alokácií na kritickej ceste.
- **C/C++ SDK (`sdk/c_cpp/libsymbolon`)**: Natívna C knižnica pre integráciu do CAD aplikácií, herných enginov a desktopových programov.
- **WebAssembly JavaScript SDK (`sdk/wasm/symbolon-validator.js`)**: Modul pre webové a hybridné aplikácie.

#### Webové Rozhranie Control Plane & Retro TUI (`Symbolon.ControlPlane`)
- **Jednookenné Pracovné Prostredie**: Celá správa servera prebieha v jedinom okne bez nežiaducich vyskakovacích okien.
- **Retro FoxPro / DOS TUI Vzhľad**: Bridlicové pozadie, kaskádové prekrývajúce sa okná, sýte pravouhlé tiene (`box-shadow: 10px 10px 0 #000`), žlté akcelerátory a azúrové zvýraznenie.
- **100% Klávesnicové Ovládanie**: Kompletná navigácia cez šípky `↑/↓/←/→`, `Enter`, `Escape`, `Tab`, priame písmenové skratky (hotkeys `V`, `L`, `K`, `R`, `M`, `A`, `G`, `S`, `T`, `O`) a funkčné klávesy `F1`–`F10`.
- **Živá Info Lišta Systémových Zdrojov**: Priebežný monitoring v reálnom čase na spodku okna zobrazujúci využitie **CPU %**, **RAM MB**, **DISK MB**, **INTERNET KB/s**, **Server IP**, **Prihláseného používateľa** a **Serverový čas**.
- **Merkle Tree Transparency Log**: Deterministický Merkle strom (RFC 6962) s matematickým overovaním inkluzívnych dôkazov (`/v1/transparency/inclusion` a `/v1/transparency/verify`).
- **Podnikový Alerting & Anomálie**: Detekcia vyčerpania kapacity ($\ge 90\%$), denial spikes a bezpečnostných incidentov s notifikáciami cez webhooks (`IAlertService`).
- **Outbound Webhook Engine**: Asynchrónne doručovanie notifikácií s HMAC-SHA256 podpisom v hlavičke `X-Symbolon-Signature`.

#### Príkazový Riadok CLI (`Symbolon.Cli`)
- **Interaktívny TUI Sprievodca**: Sprievodca inštaláciou a konfiguráciou (`symbolon setup`) cez Spectre.Console.
- **Kryptografické Príkazy**: `symbolon keys generate`, `symbolon keys export-jwks`.
- **Správa Licencií**: `symbolon license keygen`, `symbolon license issue`, `symbolon license inspect`.
- **Migračné Nástroje**: `symbolon import`, `symbolon export` pre prechod z FlexNet Publisher a Keygen.sh.
- **Súlad CRA & SBOM**: Generovanie CycloneDX v1.6 SBOM (`symbolon sbom`) a overovanie kontrolných súčtov binárok (`symbolon verify-artifact`).
- **Cloud-Native OCI & Mesh**: Balenie a verifikácia OCI balíčkov (`symbolon oci pack`, `symbolon oci verify`), zobrazenie konsenzu klastra (`symbolon mesh status`) a overenie TPM 2.0 citácií (`symbolon attestation verify`).
- **Diagnostika**: Kontrola prostredia, kryptografie a pripojenia (`symbolon doctor`).

#### Legislatívny a Bezpečnostný Súlad
- **EU Cyber Resilience Act (CRA - Nariadenie 2024/2847)**: Endpointy `/v1/compliance/cra` a strojovo čitateľný súlad.
- **CycloneDX v1.6 SBOM**: Endpoint `/v1/compliance/sbom` s kompletným zoznamom softvérových komponentov.

---

### Zmenené (Changed)

- Architektúra webového administračného rozhrania bola transformovaná na jednotné pracovné okno (Single Desktop Window Environment) s FoxPro vizuálom.
- Koordinátor distribuovaného konsenzu `RelayMeshCoordinator` bol presunutý do `Symbolon.Protocol` pre spoločné využitie v CLI, Relay aj Control Plane.
- Predvolené správanie autentifikácie API bolo zmenené na striktné bezpečné zlyhanie (fail-closed).
- Globálny rate limiter bol zmenený na partíciovaný podľa IP adresy klienta.

---

### Opravené (Fixed)

- **Súbeh pri prideľovaní sedadiel**: Vyriešený concurrency race condition pri paralelnom checkoute licencií odstránením nekonzistentných medzistavov.
- **Deadlock vo vlákne obnovy**: Opravená synchronizácia pozadového časovača v .NET a Python SDK pri obnove expirovaných lease tokenov.
- **Parsovanie ISO 8601 trvaní**: Opravená konverzia hodnôt časového posunu (clock skew tolerance) v `PolicyClaim`.
- **Uvoľňovanie prostriedkov**: Zabezpečené korektné uzatváranie streamovacích WebSocket a gRPC spojení pri zlyhaní siete.

---

### Bezpečnosť (Security)

V rámci nezávislého bezpečnostného auditu (Senior Cyber Security Auditor) boli identifikované a kompletne odstránené nasledujúce zraniteľnosti (SEC-01 až SEC-06):

- **SEC-01 (Insecure Default: Dev SuperAdmin Bypass)**: Odstránený nekontrolovaný bypass autentifikácie v `ApiKeyAuthenticationHandler.cs`. Server je teraz v predvolenej inštalácii striktne uzamknutý (fail-closed).
- **SEC-02 (32-bit Truncated Hash Collision)**: V `PublicEndpoints.cs` a `OfflineEndpoints.cs` bola zavedená striktná kontrola celého 256-bitového `KeyHash` pomocou `CryptographicOperations.FixedTimeEquals`, čím sa eliminovalo riziko kolíznych útokov na skrátený 32-bitový index.
- **SEC-03 (Server-Side Request Forgery - SSRF)**: Zavedený prísny validátor webhookových adries `WebhookSecurityValidator`, ktorý blokuje link-local adresy (AWS IMDS `169.254.0.0/16`), loopback (`127.0.0.0/8`), multicast a privátne siete RFC 1918 v produkčnom režime.
- **SEC-04 (Broken Object Level Authorization - BOLA)**: Zabezpečená striktná izolácia nájomcov (multi-tenancy boundary enforcement) v `AdminEndpoints.cs` a `ApiKeyEndpoints.cs`. Používatelia bez roly `admin:super` nemôžu čítať ani modifikovať dáta iných nájomcov.
- **SEC-05 (Denial of Service cez globálny Rate Limiter)**: Prechod z globálneho zdieľaného rate limitera na particionované limitery podľa IP adresy pripojenia klienta a identity volajúceho.
- **SEC-06 (Chýbajúce HTTP Security Headers)**: Zavedený bezpečnostný middleware vstrekujúci prísne hlavičky `Content-Security-Policy`, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, `Permissions-Policy` a `Strict-Transport-Security` (HSTS).

---

[1.0.0]: https://github.com/MAGORS-organisation/License-server/releases/tag/v1.0.0
