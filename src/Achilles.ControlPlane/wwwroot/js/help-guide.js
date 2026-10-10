/**
 * ==============================================================================
 * Achilles Enterprise Floating License Server
 * Komplexná Používateľská & Administrátorská Príručka (Interactive Help Guide)
 * Plnohodnotná dokumentácia prístupná priamo z aplikačného menu & F1
 * ==============================================================================
 */

(function () {
    'use strict';

    // Dáta komplexnej príručky systému Achilles
    const helpGuideData = {
        categories: [
            { id: 'all', label: 'Všetky Témy', icon: '📚' },
            { id: 'core', label: 'Základ & Architektúra', icon: '🏛️' },
            { id: 'floating', label: 'Plávajúce Licencie & Lízing', icon: '💺' },
            { id: 'crypto', label: 'Kryptografia & PQC', icon: '⚛️' },
            { id: 'airgap', label: 'Relay, Air-Gap & Siete', icon: '🛰️' },
            { id: 'security', label: 'eBPF & Bezpečnosť', icon: '🛡️' },
            { id: 'enterprise', label: 'Enterprise & Identity', icon: '🏢' },
            { id: 'sdk', label: 'SDK & Vývojári', icon: '💻' },
            { id: 'operations', label: 'CLI & Prevádzka', icon: '⚙️' }
        ],

        chapters: [
            {
                id: 'ch-intro',
                number: '01',
                category: 'core',
                title: 'Úvod a Celková Architektúra Systému',
                icon: '🏛️',
                badge: 'Základ',
                summary: 'Kompletný prehľad ekosystému Achilles, princípy fungovania, rozdelenie komponentov, topológia ControlPlane/Relay a formáty artefaktov.',
                targetView: 'view-overview',
                targetViewName: 'Prehľad Servera',
                content: `
<h3>1.1 Čo je Achilles Enterprise Floating License Server?</h3>
<p>
<strong>Achilles</strong> je moderný vysokoobrátkový podnikový licenčný server novej generácie navrhnutý špeciálne pre nezávislých dodávateľov softvéru (ISV), priemyselnú automatizáciu, CAD/CAM/CAE inžinierske softvéry a cloudové aj on-premise prostredia.
Rieši kľúčové nedostatky historických systémov ako FlexNet Publisher (FLEXlm), Sentinel RMS či RLM:
</p>
<ul>
    <li><strong>Nulové distribuované zámky:</strong> Využíva deterministické prideľovanie sedadiel v čase <code>O(1)</code> s materializovanými sedadlami a <code>FOR UPDATE SKIP LOCKED</code>.</li>
    <li><strong>Hybridná Post-Kvantová Kryptografia (PQC):</strong> Podpora FIPS 204 (ML-DSA-65) a FIPS 203 (ML-KEM) v kombinácii s klasickým NIST P-256 (ES256) pre ochranu pred útokmi typu <em>Harvest Now, Decrypt Later</em>.</li>
    <li><strong>Autonómny On-Premise Relay uzol:</strong> Lokálny proxy agent s delegovaným blokom sedadiel (Seat Grant) fungujúci 100% autonómne aj pri úplnom prerušení internetového spojenia.</li>
    <li><strong>Striktná Air-Gap podpora:</strong> Výmena offline požiadaviek <code>.symreq</code> a poverení <code>.symgrant</code> cez USB médiá s auditným Merkle hash stromom.</li>
    <li><strong>Ochrana v jadre Linuxu (eBPF):</strong> Nízkoúrovňová kontrola sieťových socketov priamo v BPF subsystéme blokujúca neoprávnený softvér bez licencie v sub-mikrosekundovom čase.</li>
</ul>

<h3>1.2 Architektonické Komponenty a Topológia</h3>
<table class="table" style="width: 100%; margin: 12px 0;">
    <thead>
        <tr><th>Komponent</th><th>Technológia</th><th>Rola a Zodpovednosť</th></tr>
    </thead>
    <tbody>
        <tr><td><strong>Achilles ControlPlane</strong></td><td>.NET 10 LTS / PostgreSQL 17</td><td>Centrálna autorita, vydávanie licencií, rotácia kľúčov, SCIM synchronizácia, REST API a Web TUI.</td></tr>
        <tr><td><strong>Achilles Relay</strong></td><td>.NET 10 / SQLite WAL</td><td>Lokálny zástupca v privátnej sieti zákazníka, spracovanie tisícov lokálnych heartbeatov/s pri nulovej cloudovej latencii.</td></tr>
        <tr><td><strong>Klientske SDK</strong></td><td>C#, Python, Rust, C++, Go, Wasm</td><td>Klientske knižnice integrujúce automatický heartbeat na pozadí, adaptívny jitter, RAII a fallback.</td></tr>
        <tr><td><strong>Achilles CLI</strong></td><td>Single-file AOT binárka</td><td>Správa kľúčov, vydávanie licencií, diagnostika (doctor), SBOM export a kontrola integrity.</td></tr>
        <tr><td><strong>Kubernetes Operator</strong></td><td>Kubernetes CRDs</td><td>Deklaratívna správa klastra (<code>SymbolonCluster</code>) a licencií (<code>SymbolonLicense</code>) v GitOps prostrediach.</td></tr>
    </tbody>
</table>

<h3>1.3 Formáty Artefaktov a Tokenov</h3>
<ul>
    <li><code>.symlic</code> — Digitálny licenčný dokument vo formáte JWS General JSON (RFC 7515) zabalený v čitateľnej PEM obálke (<code>-----BEGIN ACHILLES LICENSE-----</code>).</li>
    <li><code>.symlease</code> — Samostatne verifikovateľný lístok zapožičaného plávajúceho sedadla pre offline roaming na $N$ dní.</li>
    <li><code>.symreq</code> & <code>.symgrant</code> — Protokol delegovania kapacity pre Air-Gap uzly s disjunktným rozsahom sedadiel.</li>
    <li><code>.symrl</code> — Kryptograficky podpísaný zoznam revokovaných licencií a kľúčov (CRL).</li>
    <li><code>SYM1-XXXX-XXXX-XXXX-XXXX</code> — Licenčný kľúč v alfanumerickom kódovaní Crockford Base32 s ochranou CRC-32C.</li>
</ul>
`
            },
            {
                id: 'ch-floating',
                number: '02',
                category: 'floating',
                title: 'Plávajúce Licencie & Lízingový Mechanizmus',
                icon: '💺',
                badge: 'Jadro',
                summary: 'Princíp prideľovania sedadiel O(1), FOR UPDATE SKIP LOCKED, periodické heartbeaty, adaptívny jitter, clock skew ochrana a garancia nulového prečerpania.',
                targetView: 'view-licenses',
                targetViewName: 'Evidencia Licencií',
                content: `
<h3>2.1 Deterministické Prideľovanie Sedadiel v O(1)</h3>
<p>
Tradičné licenčné servery používajú distribuované pamäťové zámky (Redis Redlock, Raft), ktoré pri sieťových fluktuáciách trpia split-brainom a spôsobujú buď zlyhanie lízingu alebo prečerpanie (over-allocation).
Achilles využíva vzor <strong>materializovaných sedadiel</strong> v relačnej databáze:
</p>
<pre><code>-- SQL transakcia výberu sedadla (O(1) okamžitý lock)
SELECT id, seat_number 
FROM seats 
WHERE license_id = @licenseId AND status = 'available'
LIMIT 1
FOR UPDATE SKIP LOCKED;</code></pre>
<p>
Tento prístup garantuje <strong>matematicky presne 0 prečerpaných licencií</strong> pri tisíckach paralelných súbežných dopytov bez nutnosti externej distribuovanej synchronizácie.
</p>

<h3>2.2 Heartbeat Protokol, TTL a Adaptívny Jitter</h3>
<p>
Po úspešnom checkoute (<code>POST /v1/leases</code>) obdrží klient podpísaný lease token s obmedzenou dobou platnosti (Time-To-Live, štandardne 300 sekúnd).
Klientske SDK spúšťa vlákno na pozadí, ktoré v pravidelnom intervale (<code>TTL / 2</code>, t.j. každých 150 sekúnd) odosiela obnovovaciu požiadavku (<code>POST /v1/leases/{id}/renew</code>).
</p>
<div class="alert-box" style="background: rgba(59, 130, 246, 0.1); border-left: 4px solid var(--accent-primary); padding: 12px; margin: 12px 0;">
    <strong>Adaptívny ±10% Jitter (Anti-Thundering Herd):</strong><br>
    Aby sa predišlo zahlteniu siete v momente, keď sa ráno naraz pripojí 5 000 inžinierov, každé klientske SDK automaticky randomizuje čas odoslania heartbeatu o ±10%.
</div>

<h3>2.3 Ochrana Pred Posunom Hodín (Clock Skew) a Monotónna Sekvencia</h3>
<p>
Každá obnovovacia požiadavka obsahuje prísne monotónne rastúce počítadlo sekvencie <code>seq</code>. Ak sa pokúsi škodlivý proces poslať starý heartbeat, server ho odmietne.
Server zároveň porovnáva klientsky čas s časom servera — ak je zistený posun hodín väčší ako 5 minút, systém aktivuje ochranu proti manipulácii s expiráciou a zaznamená bezpečnostný záznam do auditného ledgeru.
</p>
`
            },
            {
                id: 'ch-queue',
                number: '03',
                category: 'floating',
                title: 'Licenčný Rad & Prioritná Alokácia (Queue FLT-31)',
                icon: '⏳',
                badge: 'Fronta',
                summary: 'Riešenie situácií pri vyčerpaní všetkých sedadiel. VIP váhy, prioritné zaraďovanie, adaptívny polling a okamžité uvoľnenie čakajúcim procesom.',
                targetView: 'view-queue',
                targetViewName: 'Licenčný Rad',
                content: `
<h3>3.1 Ako Funguje Licenčný Rad (FLT-31)?</h3>
<p>
Ak sú všetky plávajúce sedadlá licencie obsadené, klient nemusí okamžite skončiť chybou <em>License Exhausted</em>.
Môže požiadať o zaradenie do prioritného radu (<code>POST /v1/queue/join</code>).
Klient dostane unikátny <code>ticketId</code> a predpokladaný čas čakania (ETA).
</p>

<h3>3.2 Prioritné Váhy Používateľov (VIP Tiering)</h3>
<table class="table" style="width: 100%; margin: 12px 0;">
    <thead>
        <tr><th>Úroveň Priority</th><th>Váha (Weight)</th><th>Typické Využitie</th></tr>
    </thead>
    <tbody>
        <tr><td><strong>VIP / Executive</strong></td><td><code>100 - 200</code></td><td>Vedúci konštrukcie, kritické simulačné výpočty, výrobná linka.</td></tr>
        <tr><td><strong>Standard Engineer</strong></td><td><code>50</code></td><td>Bežní inžinieri a projektanti pracujúci v CAD.</td></tr>
        <tr><td><strong>Batch / CI-CD Job</strong></td><td><code>10</code></td><td>Automatické nočné testy a generovanie výkresov (nízka priorita).</td></tr>
    </tbody>
</table>

<h3>3.3 Automatický Reaper & Expirácia Lístkov</h3>
<p>
Vstavaný <code>QueueReaperBackgroundService</code> na pozadí monitoruje opustené lístky (klienti, ktorí havarovali a prestali sa pýtať na stav).
Ak klient nepotvrdí záujem do 60 sekúnd, lístok je uvoľnený a sedadlo je okamžite ponúknuté ďalšiemu čakateľovi.
</p>
`
            },
            {
                id: 'ch-pqc',
                number: '04',
                category: 'crypto',
                title: 'Post-Kvantová Kryptografia (PQC) & Správa Kľúčov',
                icon: '⚛️',
                badge: 'Kryptografia',
                summary: 'Hybridné podpisovanie ES256 + ML-DSA-65 (FIPS 204), FIPS 203 (ML-KEM), striktný mód pqc-strict, rotácia kľúčov a PQC vulnerability scanner.',
                targetView: 'view-pqc',
                targetViewName: 'Post-Quantum Era',
                content: `
<h3>4.1 Prečo Post-Kvantová Kryptografia Už Dnes?</h3>
<p>
Útočníci v súčasnosti ukladajú zašifrovanú sieťovú komunikáciu a licenčné súbory s cieľom prelomiť ich o niekoľko rokov pomocou kvantového počítača (hrozba <strong>Harvest Now, Decrypt Later</strong>).
Achilles ako prvý licenčný server na trhu implementuje oficiálne štandardy NIST Post-Quantum Cryptography schválené v auguste 2024.
</p>

<h3>4.2 Podporované Štandardy a Algoritmy</h3>
<ul>
    <li><strong>FIPS 204 (ML-DSA-65):</strong> Digitálne podpisy založené na mriežkovej kryptografii (Module-Lattice Digital Signature Algorithm). Garantuje bezpečnosť aj pred útokmi Shorovho algoritmu.</li>
    <li><strong>FIPS 203 (ML-KEM-768/1024):</strong> Mechanizmus enkapsulácie kľúčov pre hybridný mTLS a transport dát.</li>
    <li><strong>FIPS 205 (SLH-DSA):</strong> Bezstavové hashovacie podpisy (Stateless Hash-Based Signatures) ako matematicky nezávislá poistka pre prípad objavenia slabín v mriežkach.</li>
    <li><strong>NIST P-256 (ES256):</strong> Klasická eliptická krivka zachovaná v hybridnom režime pre 100% spätnú kompatibilitu so staršími operačnými systémami a mikrokontrolérmi.</li>
</ul>

<h3>4.3 Prepínanie Režimov Kryptografie</h3>
<pre><code># Nastavenie v .env alebo systémovom prostredí:
ACHILLES_PQC_PROFILE=hybrid-v1   # Predvolené: ES256 + ML-DSA-65 (Dual Signature)
ACHILLES_PQC_PROFILE=pqc-strict  # Striktný mód: Výlučne ML-DSA-65 (CNSA 2.0 / NIS 2 súlad)</code></pre>

<h3>4.4 Bezvýpadková Rotácia Kľúčov (Zero-Downtime Key Rotation)</h3>
<p>
Rotácia kľúča sa spúšťa cez <code>POST /admin/v1/keys/rotate</code>.
Nový kľúč je okamžite publikovaný v JWKS endpointe <code>/v1/.well-known/achilles-keys</code>.
Staré kľúče prechádzajú do stavu <code>deprecated</code>, kedy sa nimi už nové licencie nepodpisujú, ale stále sa overujú existujúce platné lízingy až do ich vypršania.
V prípade kompromitácie je kľúč okamžite označený ako <code>revoked</code> a publikovaný v revokačnom zozname.
</p>
`
            },
            {
                id: 'ch-airgap',
                number: '05',
                category: 'airgap',
                title: 'On-Premise Relay, Air-Gap & Poverenia (.symreq / .symgrant)',
                icon: '🛰️',
                badge: 'Air-Gap',
                summary: 'Riešenie pre izolované priemyselné závody, jadrové elektrárne a vojenské prostredia. Generovanie požiadaviek, disjunktná alokácia a USB transfer.',
                targetView: 'view-airgap',
                targetViewName: 'Air-Gap Portál',
                content: `
<h3>5.1 Princíp Delegovaného Seat Grantu</h3>
<p>
V striktne izolovaných výrobných prevádzkach bez prístupu na internet (Air-Gapped Zones) nie je možné kontaktovať centrálny cloudový server.
Historické riešenia používali neohrabané dongle kľúče alebo nespoľahlivé trojice serverov (FlexNet triads).
Achilles prináša kryptografický <strong>Delegated Seat Grant protokol</strong>:
</p>
<ol>
    <li>Lokálny Relay server v izolovanom závode vygeneruje kryptograficky podpísanú žiadosť <code>.symreq</code> (požiadavka na $N$ sedadiel na dobu 30 dní).</li>
    <li>Správca prenesie súbor <code>.symreq</code> na USB kľúči k počítaču s internetom a nahrá ho do <strong>Air-Gap Portálu</strong> v Achilles ControlPlane.</li>
    <li>ControlPlane deterministicky alokuje <strong>disjunktný rozsah sedadiel</strong> (napr. sedadlá 1 až 25) a vystaví podpísané poverenie <code>.symgrant</code>.</li>
    <li>Správca nahrá <code>.symgrant</code> späť na USB kľúč a importuje ho do lokálneho Relay servera.</li>
    <li>Relay server beží plnohodnotne offline na lokálnej sieti LAN, kým neuplynie delegovaná doba.</li>
</ol>

<h3>5.2 Disjunktná Alokácia Sedadiel (Disjunctive Allocation)</h3>
<p>
ControlPlane garantuje, že rozsah sedadiel pridelený izolovanému závodu <code>[SeatFrom..SeatTo]</code> je v centrálnej databáze dočasne zablokovaný pre iných klientov.
Tým je zabezpečená <strong>100% záruka nulového prekrývania a neprečerpania licencií</strong> aj v distribuovanom air-gapped prostredí.
</p>
`
            },
            {
                id: 'ch-fingerprint',
                number: '06',
                category: 'security',
                title: 'Hardvérový Odtlačok (Fingerprint) & Node-Locking',
                icon: '💻',
                badge: 'Hardware',
                summary: 'Zber 6 hardvérových komponentov, fuzzy matching stratégie, anti-virtualizácia, TPM 2.0 PCR attestation a viazanie licencií na stanice.',
                targetView: 'view-machines',
                targetViewName: 'Node-Lock & Stroje',
                content: `
<h3>6.1 Multi-Component Hardvérový Odtlačok</h3>
<p>
Pre viazanie licencie na konkrétny fyzický počítač (Node-Locked licencia) zbiera Achilles SDK stabilný hardvérový fingerprint zložený zo 6 nezávislých komponentov:
</p>
<ul>
    <li><code>machineId</code> — Unikátne systémové GUID (Windows MachineGuid / Linux /etc/machine-id / macOS IOPlatformUUID).</li>
    <li><code>board</code> — Sériové číslo základnej dosky zo SMBIOS / DMI tabuliek.</li>
    <li><code>cpu</code> — Identifikátor procesora, revízia a inštrukčné ID (CPUID).</li>
    <li><code>disk</code> — Sériové číslo primárneho fyzického diskového radiča (NVMe / SSD serial).</li>
    <li><code>mac</code> — Fyzická MAC adresa primárneho sieťového adaptéra (s ignorovaním virtuálnych adaptérov).</li>
    <li><code>host</code> — Kryptograficky solený pseudonymizovaný hash názvu počítača (GDPR compliant).</li>
</ul>

<h3>6.2 Fuzzy Matching Stratégie (Tolerancia Výmeny Hardvéru)</h3>
<p>
Keď používateľ v počítači vymení sieťovú kartu alebo disk, bežné licencie zlyhajú. Achilles podporuje 4 inteligentné matching stratégie:
</p>
<ul>
    <li><code>match-all</code> — Striktná zhoda: všetkých 6 komponentov musí byť identických (maximálna bezpečnosť).</li>
    <li><code>match-most</code> (predvolené) — Väčšinová zhoda: aspoň >50% komponentov musí sedieť (vhodné pre bežné kancelárske stanice).</li>
    <li><code>match-two</code> — Zhodujú sa aspoň 2 kľúčové komponenty (napr. doska + disk).</li>
    <li><code>match-any</code> — Stačí zhoda v jednom komponente.</li>
</ul>

<h3>6.3 Anti-Virtualizácia & TPM 2.0 Enclave Attestation</h3>
<p>
Achilles automaticky detekuje beh vo vnútri Docker kontajnera, Kubernetes podu alebo hypervízora (VMware, VirtualBox, Hyper-V, KVM).
Pre vysoko zabezpečené prostredia podporuje kryptografické overovanie <strong>TPM 2.0 PCR Quote</strong> s challenge-response noncom, čím sa zabráni klonovaniu licencií cez snapshoty virtuálnych strojov.
</p>
`
            },
            {
                id: 'ch-ebpf',
                number: '07',
                category: 'security',
                title: 'eBPF Kernel Socket Enforcement (Ochrana v Jadre Linuxu)',
                icon: '🛡️',
                badge: 'Kernel',
                summary: 'Nízkoúrovňová ochrana na úrovni jadra Linuxu. BPF CO-RE socket hooks, okamžité zablokovanie nepovolených spojení a ring-buffer audit.',
                targetView: 'view-system',
                targetViewName: 'Diagnostika & eBPF',
                content: `
<h3>7.1 Prečo eBPF na Úrovni Jadra?</h3>
<p>
Používateľské aplikácie môžu byť dekompilované, modifikované alebo ich sieťová komunikácia môže byť oklamaná cez lokálne proxy.
Vďaka podpore <strong>eBPF (Extended Berkeley Packet Filter)</strong> dokáže Achilles vynucovať platnosť licencie priamo v jadre Linuxu:
</p>
<ul>
    <li>BPF CO-RE program sa pripája na cgroup socket hooks (<code>cgroup/connect4</code> a <code>cgroup/connect6</code>).</li>
    <li>Pri každom pokuse chránenej aplikácie nadviazať sieťové spojenie nahliadne jadro do BPF mapy <code>license_map</code>.</li>
    <li>Ak proces nemá platný lízing, jadro okamžite zhodí spojenie (návratový kód <code>-EPERM</code> alebo <code>BPF_DROP</code>) bez prebudenia userspace.</li>
    <li>Overenie trvá rádovo <strong>menej ako 200 nanosekúnd</strong> bez akejkoľvek merateľnej réžie.</li>
</ul>

<h3>7.2 Správa Cez CLI a Monitoring</h3>
<pre><code># Zistenie stavu eBPF subsystému na Linuxe:
achilles ebpf status

# Pripojenie licenčného socket filtra k cgroup:
achilles ebpf attach --cgroup /sys/fs/cgroup/engineering --port 8080

# Živý výpis zachytených narušení z jadrového ring-buffera:
achilles ebpf violations --follow</code></pre>
`
            },
            {
                id: 'ch-georepl',
                number: '08',
                category: 'airgap',
                title: 'Multi-Region Geo-Replikácia & Active-Active CRDT',
                icon: '🌐',
                badge: 'Klastre',
                summary: 'Globálne klastre bez centrálneho distribuovaného zámku. Vektorové hodiny, PN-Counter CRDT a okamžité lokálne checkouts v EU, US aj AP.',
                targetView: 'view-mesh',
                targetViewName: 'Relay Mesh & Klastre',
                content: `
<h3>8.1 Architektúra Bez Centrálneho Bodu Zlyhania</h3>
<p>
Pre nadnárodné korporácie s pobočkami v Európe (Frankfurt), Severnej Amerike (Virgínia) a Ázii (Singapur) je latencia transatlantických databázových zámkov neakceptovateľná.
Achilles Geo-Replication implementuje <strong>bezkolízny dátový typ PN-Counter CRDT (Conflict-Free Replicated Data Type)</strong>:
</p>
<ul>
    <li>Každý región má priradený autonómny rozsah sedadiel a vlastné vektorové hodiny (Vector Clock).</li>
    <li>Lokálni inžinieri v Tokiu získajú sedadlo za <strong>menej ako 2 milisekundy</strong> priamo z tokijského uzla.</li>
    <li>Zmeny stavu sú asynchrónne replikované cez zabezpečený mTLS kanál s HMAC-SHA256 integritou.</li>
    <li>Pri výpadku transoceánskeho kábla pokračujú všetky regióny v autonómnej prevádzke; po obnovení spojenia prebehne automatické deterministické zlúčenie stavov (Partition Healing).</li>
</ul>
`
            },
            {
                id: 'ch-identity',
                number: '09',
                category: 'enterprise',
                title: 'Podnikové Identity (SCIM 2.0) & Single Sign-On (SSO)',
                icon: '👥',
                badge: 'Identity',
                summary: 'Integrácia s Microsoft Entra ID (Azure AD), Okta a Keycloak. SCIM 2.0 zriaďovanie, SAML 2.0, OIDC PKCE a okamžitá revokácia sedadiel.',
                targetView: 'view-scim',
                targetViewName: 'SCIM 2.0 Identity',
                content: `
<h3>9.1 SCIM 2.0 Automatizovaný Identity Lifecycle</h3>
<p>
Achilles podporuje štandardy <strong>RFC 7643 a RFC 7644 (SCIM 2.0)</strong> na koncovom bode <code>/scim/v2</code>.
Keď IT administrátor pridá zamestnanca do skupiny v Microsoft Entra ID alebo Okta, používateľ je automaticky vytvorený v Achilles:
</p>
<div class="alert-box" style="background: rgba(239, 68, 68, 0.1); border-left: 4px solid #ef4444; padding: 12px; margin: 12px 0;">
    <strong>Zero-Trust Okamžité Uvoľnenie Sedadiel (Deprovisioning):</strong><br>
    V momente, keď je zamestnanec deaktivovaný v podnikovom IdP, Achilles okamžite uvoľní všetky jeho aktívne plávajúce sedadlá, zruší čakajúce požiadavky v rade a zablokuje ďalšie checkouts.
</div>

<h3>9.2 SAML 2.0 & OIDC Single Sign-On</h3>
<p>
Správcovia a inžinieri sa môžu prihlasovať do Web TUI pomocou podnikového SSO:
</p>
<ul>
    <li><strong>OIDC Core 1.0:</strong> Authorization Code Flow s PKCE (RFC 7636) a ochranou proti CSRF.</li>
    <li><strong>SAML 2.0:</strong> Podpora pre SP-initiated aj IdP-initiated prihlásenie s bezpečnou XML validáciou (ochrana pred XXE).</li>
    <li><strong>RBAC Claim Mapping:</strong> Automatická konverzia IdP skupín (napr. <code>Licensing-Admins</code>) na interné roly <code>admin:super</code>, <code>admin:tenant</code> alebo <code>auditor</code>.</li>
</ul>
`
            },
            {
                id: 'ch-kms',
                number: '10',
                category: 'crypto',
                title: 'Správa Kľúčov (Cloud KMS, Hardware HSM & Shamir Sharing)',
                icon: '🔐',
                badge: 'KMS & HSM',
                summary: '3-úrovňová hierarchia kľúčov (§9.3), integrácia s Azure Key Vault / AWS KMS, PKCS#11 Hardware HSM a Shamir k-of-n prahová obnova.',
                targetView: 'view-keys',
                targetViewName: 'Podpisové Kľúče',
                content: `
<h3>10.1 3-Úrovňová Hierarchia Kľúčov (§9.3)</h3>
<p>
V súlade s bezpečnostnými štandardmi využíva Achilles trojstupňovú hierarchiu kľúčov:
</p>
<ol>
    <li><strong>Tier 1 — Root Master Anchor:</strong> Cold/Offline kľúč s platnosťou 10 rokov uložený v offline HSM trezore. Podpisuje certifikáty autorít Tier 2.</li>
    <li><strong>Tier 2 — Intermediate Product Authority:</strong> Kľúč pre produktovú líniu (platnosť 2 roky) uložený v Cloud KMS (Azure Key Vault / AWS KMS / Hardware HSM). Podpisuje samotné licencie <code>.symlic</code>.</li>
    <li><strong>Tier 3 — Ephemeral Lease Key:</strong> Rýchly efemérny kľúč uzla (platnosť 30 dní) používaný na podpisovanie lízingových tokenov pre klientov.</li>
</ol>

<h3>10.2 Shamir's Secret Sharing ($k$-of-$n$ Obnova pri Havárii)</h3>
<p>
Pre prípad straty kľúča alebo havárie dátového centra podporuje Achilles informačno-teoreticky bezpečné delenie privátnych kľúčov nad Galoisovým poľom $GF(2^8)$:
</p>
<pre><code># Rozdelenie master kľúča na 5 častí s prahom 3 (stačia ľubovoľné 3 časti na obnovu):
achilles keys split --key ./master-priv.jwk --parts 5 --threshold 3 --out ./shares/

# Rekonštrukcia kľúča z 3 častí:
achilles keys combine --parts ./shares/part1.share,./shares/part3.share,./shares/part5.share --out restored.jwk</code></pre>
`
            },
            {
                id: 'ch-features',
                number: '11',
                category: 'floating',
                title: 'Granulárne Moduly (Entitlements) & Balíkové Suity',
                icon: '🧩',
                badge: 'Moduly',
                summary: 'Nezávislé kvóty pre funkcie a add-ony, balíkové suity s verzovaním, dynamická alokácia za behu a RAII správa vo viacjazyčných SDK.',
                targetView: 'view-features',
                targetViewName: 'Moduly & Funkcie',
                content: `
<h3>11.1 Nezávislé Kvóty Modulov a Add-onov</h3>
<p>
Veľké inžinierske softvéry majú základnú aplikáciu (napr. CAD Designer s 50 sedadlami) a drahé špecializované moduly (napr. FEA Solver s iba 5 sedadlami, Renderer s 10 sedadlami).
Achilles umožňuje definovať nezávislé kvóty pre každý modul v rámci jednej licencie:
</p>
<ul>
    <li>Klient si spraví checkout základnej licencie (1 sedadlo).</li>
    <li>Keď inžinier klikne na tlačidlo "Spustiť pevnostný výpočet", SDK dynamicky požiada o modul: <code>POST /v1/leases/{id}/features/acquire</code> s parametrom <code>FEA_SOLVER</code>.</li>
    <li>Po dokončení výpočtu sa modul okamžite uvoľní späť do fondu, zatiaľ čo základné sedadlo CAD Designer zostáva inžinierovi pridelené.</li>
</ul>

<h3>11.2 RAII Použitie v Kóde</h3>
<pre><code>// C# (.NET 10) - Automatické uvoľnenie modulu po opustení bloku using
await using (var fea = await lease.UseFeatureAsync("FEA_SOLVER"))
{
    // Výkonný výpočet v solveri...
} // Tu sa FEA_SOLVER automaticky vráti do fondu sedadiel!</code></pre>
`
            },
            {
                id: 'ch-policy',
                number: '12',
                category: 'enterprise',
                title: 'Zásady & Pravidlá Politík (Options Engine FLT-24)',
                icon: '📜',
                badge: 'Pravidlá',
                summary: 'Deterministické vyhodnocovanie deny ➜ max ➜ reserve ➜ priority. Rezervácie pre tímy, CIDR masky podsietí a živý simulátor pravidiel.',
                targetView: 'view-policy-rules',
                targetViewName: 'Pravidlá & Options',
                content: `
<h3>12.1 Deterministické Vyhodnocovanie (Striktné Poradie)</h3>
<p>
Achilles vyhodnocuje licenčné pravidlá v prísnom deterministickom poradí podľa normatívnej špecifikácie:
</p>
<ol>
    <li><code>deny</code> — Ak používateľ, stroj alebo IP maska spĺňa pravidlo zákazu, operácia okamžite končí s chybou <code>403 Forbidden (RuleDenied)</code>.</li>
    <li><code>max</code> — Obmedzuje maximálny počet súbežných sedadiel pre danú skupinu (napr. študenti max 5 sedadiel).</li>
    <li><code>reserve</code> — Garantuje vyhradené sedadlá pre konkrétne tímy (napr. tím <code>eng-safety</code> má vždy garantované aspoň 3 sedadlá).</li>
    <li><code>priority</code> — Zaraďuje požiadavky do licenčného radu podľa váhy používateľa.</li>
</ol>

<h3>12.2 Podpora CIDR Podsietí a Wildcard Masiek</h3>
<pre><code># Príklad pravidiel v YAML konfigurácii:
rules:
  - type: deny
    ipRange: "192.168.100.0/24"
    reason: "Hostia na Wi-Fi sieti nesmú čerpať podnikové licencie"
  - type: reserve
    userGroup: "senior-calculators"
    seats: 4
  - type: max
    userWildcard: "intern-*"
    maxSeats: 2</code></pre>
`
            },
            {
                id: 'ch-tokens',
                number: '13',
                category: 'floating',
                title: 'Tokenové & Pay-As-You-Go Licencovanie',
                icon: '🪙',
                badge: 'Kredity',
                summary: 'Kreditové peňaženky pre CAD/HPC výpočty a AI inferenciu. Dvojfázové rezervovanie 2PC, sadzobník za čas/úlohu a nemenný ledger.',
                targetView: 'view-tokens',
                targetViewName: 'Tokeny & Kredity',
                content: `
<h3>13.1 Kedy Použiť Tokenové Licencovanie?</h3>
<p>
Pre náročné nepravidelné úlohy (napr. crash-test simulácie automobilu trvajúce 4 hodiny na 64 jadrách alebo trénovanie AI modelov) nie je model stálych plávajúcich sedadiel optimálny.
Achilles ponúka kreditový a tokenový licenčný motor:
</p>
<ul>
    <li><strong>Kreditová Peňaženka (TokenWallet):</strong> Každý zákazník/tenant má peňaženku s kreditným zostatkom a voliteľným limitom prečerpania (Overdraft Limit).</li>
    <li><strong>Flexibilný Sadzobník (TokenRate):</strong> Sadzba za minútu behu (napr. 0.5 kreditu / min) alebo za vypočítanú jednotku (napr. 10 kreditov / simulácia).</li>
    <li><strong>Dvojfázové Rezervovanie (2PC):</strong>
        <ol>
            <li><code>reserve</code> — Pred začiatkom výpočtu sa v peňaženke zablokuje odhadovaný obnos kreditov.</li>
            <li><code>heartbeat</code> — Priebežne predlžuje platnosť rezervácie počas behu úlohy.</li>
            <li><code>commit</code> — Po úspešnom skončení výpočtu sa zaúčtuje presná reálna spotreba.</li>
            <li><code>rollback</code> — Ak výpočet zlyhal alebo bol zrušený, celá rezervácia sa okamžite bezplatne vráti do peňaženky.</li>
        </ol>
    </li>
</ul>
`
            },
            {
                id: 'ch-borrow',
                number: '14',
                category: 'floating',
                title: 'Offline Roaming & Zapožičanie Licencií (Borrowing)',
                icon: '💼',
                badge: 'Roaming',
                summary: 'Dlhodobá výpožička sedadla na 1 až 30 dní pre terénne notebooky. Samostatný artefakt .symlease a kryptografické predčasné vrátenie.',
                targetView: 'view-overview',
                targetViewName: 'Prehľad & Výpožičky',
                content: `
<h3>14.1 Offline Výpožička Sedadla na Cesty</h3>
<p>
Keď inžinier cestuje na stavbu, do bane alebo do lietadla bez internetového pripojenia, môže si zapožičať plávajúce sedadlo:
</p>
<ul>
    <li>Klient požiada o výpožičku na stanovený počet dní (napr. 14 dní): <code>POST /v1/leases/{id}/borrow</code>.</li>
    <li>Server vygeneruje a podpíše samostatne overiteľný súbor <code>.symlease</code> s expiráciou na 14 dní.</li>
    <li>Sedadlo je v centrálnom fonde označené ako zapožičané (borrowed) a klientske SDK vypne odosielanie heartbeatov.</li>
    <li>Aplikácia na notebooku funguje 100% offline až do vypršania platnosti bez nutnosti sieťového kontaktu.</li>
</ul>

<h3>14.2 Predčasné Kryptografické Vrátenie (Proof-of-Possession)</h3>
<p>
Ak sa inžinier vráti z cesty po 3 dňoch, môže sedadlo vrátiť predčasne:
</p>
<div class="alert-box" style="background: rgba(16, 185, 129, 0.1); border-left: 4px solid #10b981; padding: 12px; margin: 12px 0;">
    <strong>Kryptografická Výzva (Challenge-Response):</strong><br>
    Aby nemohol niekto vrátiť sedadlo a zároveň ho naďalej používať offline na inom notebooku, server vygeneruje jednorazový kryptografický nonce.
    Klient ho musí podpísať privátnym kľúčom vytvoreným pri zapožičaní. Bez tohto dôkazu server predčasné vrátenie odmietne.
</div>
`
            },
            {
                id: 'ch-experiments',
                number: '15',
                category: 'core',
                title: 'A/B Testovanie & Experimentačný Engine (AB-1 .. AB-15)',
                icon: '🧪',
                badge: 'A/B Test',
                summary: 'Deterministický bezstavový bucketing Murmur3, invariant nulového posunu (zero-drift), živé štatistické testy chi-square a automatický canary circuit breaker.',
                targetView: 'view-experiments',
                targetViewName: 'A/B Experimenty',
                content: `
<h3>15.1 Deterministický Bezstavový Bucketing</h3>
<p>
Achilles obsahuje vstavaný experimentačný motor umožňujúci testovať nové licenčné modely, predĺžené TTL či nové cenové plány na podmnožine klientov:
</p>
<pre><code>// Deterministické priradenie do experimentálneho variantu:
uint bucket = Murmur3Hash(licenseKey + machineId + salt) % 100;
// Garantuje, že klient zostáva v identickom variante počas celého lízingu (Zero-Drift Invariant)</code></pre>

<h3>15.2 Štatistické Vyhodnotenie a Bezpečnostný Istič</h3>
<ul>
    <li><strong>Štatistika v reálnom čase:</strong> Výpočet Z-score, p-hodnoty a testu dobrej zhody $\chi^2$ ($p > 0.05$) priamo v databáze.</li>
    <li><strong>Canary Circuit Breaker:</strong> Ak experimentálny variant spôsobí nárast chybovosti lízingu o viac ako 2%, systém ho do 100 milisekúnd automaticky odpojí a vráti všetkých klientov na kontrolný variant.</li>
</ul>
`
            },
            {
                id: 'ch-webhooks',
                number: '16',
                category: 'enterprise',
                title: 'Webhooky, Notifikácie & Životný Cyklus Licencií',
                icon: '🔔',
                badge: 'Webhooky',
                summary: 'HMAC-SHA256 podpisovanie, integrácia so Slackom a MS Teams, Dead-Letter Queue (DLQ) s replayom a automatické sledovanie expirácie.',
                targetView: 'view-webhooks',
                targetViewName: 'Webhooky & Udalosti',
                content: `
<h3>16.1 Udalosťami Riadené Notifikácie</h3>
<p>
Achilles odosiela notifikácie o dôležitých udalostiach cez zabezpečené HTTP webhooky s podpisom <code>X-Achilles-Signature: t={ts},v1={hmac_sha256}</code>:
</p>
<ul>
    <li><code>lease.denied</code> — Odmietnutie lízingu z dôvodu vyčerpania kapacity (okamžitá výzva pre obchodné oddelenie na upsell).</li>
    <li><code>fraud.detected</code> — Detekcia fyzikálne nemožnej rýchlosti presunu (Impossible Travel) alebo klonovania VM.</li>
    <li><code>license.expiring_soon</code> — Upozornenie 30, 14 a 7 dní pred expiráciou licencie.</li>
    <li><code>token.threshold_low</code> — Kreditová peňaženka klesla pod definované minimum.</li>
</ul>

<h3>16.2 Dead-Letter Queue (DLQ) & Manuálny Replay</h3>
<p>
Ak je prijímací server zákazníka nedostupný, Achilles opakuje doručenie s exponenciálnym oneskorením.
Po 5 neúspešných pokusoch je správa presunutá do <strong>Dead-Letter Queue (DLQ)</strong>, odkiaľ ju administrátor môže kedykoľvek manuálne zopakovať kliknutím na tlačidlo <em>Replay</em>.
</p>
`
            },
            {
                id: 'ch-discovery',
                number: '17',
                category: 'airgap',
                title: 'Zero-Config Discovery & Server Failover Pool',
                icon: '🔍',
                badge: 'Discovery',
                summary: 'Automatické vyhľadávanie licenčných serverov cez UDP port 7584, odolný ServerFailoverPool s cooldownom a podpora premennej ACHILLES_LICENSE_SERVER.',
                targetView: 'view-relays',
                targetViewName: 'Relay & Discovery',
                content: `
<h3>17.1 Vyhľadávanie Bez Konfigurácie (Zero-Config UDP)</h3>
<p>
V privátnych LAN sieťach nemusia vývojári ani používatelia zadávať IP adresy licenčných serverov.
Klientske SDK odošle UDP broadcast / multicast na port <code>7584 (0x1D90)</code>.
Všetky aktívne inštancie Achilles ControlPlane a Relay okamžite odpovedia so svojou URL adresou a stavom zaťaženia.
</p>

<h3>17.2 Premenné Prostredia a FlexNet Notácia</h3>
<p>
Systém podporuje štandardné podnikové premenné prostredia s automatickým rozkladom adries:
</p>
<pre><code># Notácia port@host (kompatibilná s FlexNet Publisher):
export ACHILLES_LICENSE_SERVER="8080@licence.firma.local"

# Zoznam záložných serverov pre failover (oddelené čiarkou alebo bodkočiarkou):
export ACHILLES_SERVERS="http://srv1:8080,http://srv2:8080;8080@srv3"

# Spätná kompatibilita (ak existuje staršia premenná, Achilles ju automaticky načíta):
export SYMBOLON_LICENSE_SERVER="8080@licence.firma.local"</code></pre>
`
            },
            {
                id: 'ch-migrate',
                number: '18',
                category: 'operations',
                title: 'Migrácia z FlexNet Publisher (FLEXlm) & Keygen.sh',
                icon: '🔄',
                badge: 'Migrácia',
                summary: 'Automatizovaný prevod z FlexNet Publisher: parsovanie súborov license.dat, transpilácia options.opt do JSON pravidiel, analýza logov lmgrd.log a import z Keygen.sh.',
                targetView: 'view-migrate',
                targetViewName: 'Migračný Engine',
                content: `
<h3>18.1 Bezbolestný Prechod z FlexNetu (FLEXlm)</h3>
<p>
Prechod z proprietárneho FlexNetu býva pre podniky náročný. Achilles obsahuje kompletnú sadu migračných nástrojov:
</p>
<ol>
    <li><strong>Dekódovanie <code>license.dat</code>:</strong> Automaticky načíta sekcie <code>SERVER</code>, <code>DAEMON</code>, <code>FEATURE</code>, <code>INCREMENT</code> a <code>PACKAGE</code> a prekonvertuje ich na Achilles licencie.</li>
    <li><strong>Transpilácia <code>options.opt</code>:</strong> Deterministicky prevedie direktívy <code>GROUP</code>, <code>RESERVE</code>, <code>MAX</code>, <code>INCLUDE</code> a <code>EXCLUDE</code> do moderných JSON politík.</li>
    <li><strong>Analýza logov <code>lmgrd.log</code>:</strong> Prepočíta historické záznamy <code>OUT</code>, <code>IN</code> a <code>DENIED</code>, zobrazí skutočnú špičkovú krivku súbehu a odporučí optimálny počet sedadiel pre úsporu nákladov.</li>
    <li><strong>Keygen.sh Importer:</strong> Načíta JSON exporty z cloudovej platformy Keygen.sh a vytvorí tenantov, produkty a licencie.</li>
</ol>
`
            },
            {
                id: 'ch-sdk',
                number: '19',
                category: 'sdk',
                title: 'Klientske SDK & Príklady Kódu (6 Jazykov)',
                icon: '💻',
                badge: 'SDK',
                summary: 'Oficiálne klientske knižnice pod licenciou Apache-2.0. Praktické ukážky integrácie v C# (.NET 10), Python, Rust, C99/C++, Go a WebAssembly.',
                targetView: 'view-wasm',
                targetViewName: 'Wasm Validátor',
                content: `
<h3>19.1 C# (.NET 10 LTS)</h3>
<pre><code>using Achilles.Client;

var options = new AchillesClientOptions {
    ServerUri = new Uri("http://localhost:8080"),
    LicenseKey = "ACH-9ABC-DEF2-3456-7890"
};
using var client = new AchillesClient(options);

await using var lease = await client.AcquireSeatAsync(["cad-core", "fea-solver"]);
if (lease.Acquired) {
    Console.WriteLine($"Sedadlo #{lease.SeatNo} úspešne pridelené!");
    // Kód vašej aplikácie...
}
// Po opustení bloku sa sedadlo automaticky uvoľní vďaka IAsyncDisposable!</code></pre>

<h3>19.2 Python (3.10+)</h3>
<pre><code>from achilles import AchillesClient

client = AchillesClient("http://localhost:8080", product_code="cad-pro")
with client.acquire_seat("ACH-9ABC-DEF2-3456-7890", features=["cad-core"]) as lease:
    print(f"Sedadlo #{lease.seat_number} alokované. Aplikácia beží...")
# Automatické uvoľnenie po opustení bloku with (dostupný aj alias from symbolon import ...)</code></pre>

<h3>19.3 Rust (Tokio & RAII Drop)</h3>
<pre><code>use achilles_client::AchillesClient;

let client = AchillesClient::new("http://localhost:8080", "cad-pro");
let lease = client.acquire_seat("ACH-9ABC-DEF2-3456-7890")?;
println!("Sedadlo #{} alokované!", lease.seat_number());
// RAII Drop automaticky uvoľní sedadlo pri zániku premennej lease!</code></pre>

<h3>19.4 C99 / C++17</h3>
<pre><code>#include "achilles.h"

achilles_client_t* client = NULL;
achilles_client_create("http://localhost:8080", "cad-pro", &client);

achilles_lease_t* raw_lease = NULL;
if (achilles_acquire_seat(client, "ACH-9ABC-DEF2-3456-7890", &raw_lease) == ACHILLES_OK) {
    achilles::ScopedLease lease(raw_lease); // C++ RAII wrapper
    // Výkonný kód aplikácie...
}
achilles_client_destroy(client);</code></pre>
`
            },
            {
                id: 'ch-cli',
                number: '20',
                category: 'operations',
                title: 'Kompletný Prehľad CLI Príkazov (achilles)',
                icon: '⌨️',
                badge: 'CLI',
                summary: 'Kompletná referenčná príručka príkazového riadku. Všetky príkazy, prepínače, formáty a praktické scenáre použitia.',
                targetView: 'view-system',
                targetViewName: 'Systém & Diagnostika',
                content: `
<h3>20.1 Zoznam Kľúčových Príkazov CLI</h3>
<table class="table" style="width: 100%; margin: 12px 0;">
    <thead>
        <tr><th>Príkaz</th><th>Popis a Príklad</th></tr>
    </thead>
    <tbody>
        <tr><td><code>achilles setup</code></td><td>Interaktívny TUI sprievodca pre konfiguráciu ControlPlane alebo Relay uzla.</td></tr>
        <tr><td><code>achilles key gen</code></td><td>Vygenerovanie podpisového kľúča (<code>-a es256</code> alebo hybrid <code>ml-dsa-65</code>).</td></tr>
        <tr><td><code>achilles lic issue</code></td><td>Vydanie licenčného súboru <code>.symlic</code> s definíciou sedadiel a zákazníka.</td></tr>
        <tr><td><code>achilles lic inspect</code></td><td>Zobrazenie detailov a podpisov licenčného súboru <code>.symlic</code>.</td></tr>
        <tr><td><code>achilles grant request</code></td><td>Vytvorenie offline žiadosti <code>.symreq</code> pre Air-Gap Relay server.</td></tr>
        <tr><td><code>achilles grant issue</code></td><td>Schválenie žiadosti a vystavenie poverenia <code>.symgrant</code> s rozsahom sedadiel.</td></tr>
        <tr><td><code>achilles pqc scan</code></td><td>Kompletný audit kryptografickej zraniteľnosti a PQC Readiness Index (0-100%).</td></tr>
        <tr><td><code>achilles ebpf status</code></td><td>Diagnostika Linux BPF CO-RE subsystému a pripojených filtrov.</td></tr>
        <tr><td><code>achilles doctor</code></td><td>Komplexná previerka zdravia prostredia, spojenia, databázy a kľúčov.</td></tr>
        <tr><td><code>achilles sbom</code></td><td>Export oficiálneho CycloneDX v1.6 SBOM v súlade s Cyber Resilience Act (CRA).</td></tr>
        <tr><td><code>achilles verify-artifact</code></td><td>Overenie SHA-256 integrity a podpisu binárneho artefaktu.</td></tr>
    </tbody>
</table>
`
            },
            {
                id: 'ch-config',
                number: '21',
                category: 'operations',
                title: 'Konfigurácia Servera, Docker & Kubernetes',
                icon: '⚙️',
                badge: 'DevOps',
                summary: 'Nastavenia v appsettings.json, premenné prostredia, nasadenie cez Docker Compose a produkčný Kubernetes Helm Chart.',
                targetView: 'view-config',
                targetViewName: 'Konfigurácia & Jazyk',
                content: `
<h3>21.1 Premenné Prostredia (.env)</h3>
<pre><code># Databáza PostgreSQL 17
POSTGRES_DB=achilles
POSTGRES_USER=achilles
POSTGRES_PASSWORD=achilles_super_secret_production_change_me

# Achilles ControlPlane
ConnectionStrings__AchillesDb=Host=postgres;Port=5432;Database=achilles;Username=achilles;Password=achilles_super_secret_production_change_me
CONTROLPLANE_HTTP_PORT=8080
ACHILLES_ADMIN_API_KEY=ach_adm_master_sec_2026
ACHILLES_PQC_PROFILE=hybrid-v1

# Achilles Relay (Local Node)
RELAY_HTTP_PORT=8081
RELAY_DATABASE_PATH=/app/data/achilles-relay.db
RELAY_UPSTREAM_URL=http://controlplane:8080</code></pre>

<h3>21.2 Spustenie cez Docker Compose</h3>
<pre><code># Spustenie celého produkčného stacku (Postgres, ControlPlane, Relay, Prometheus, Grafana):
docker compose up -d --build

# Zobrazenie stavu kontajnerov:
docker compose ps</code></pre>

<h3>21.3 Nasadenie v Kubernetes cez Helm</h3>
<pre><code># Inštalácia oficiálneho Achilles Helm chartu:
helm install achilles deploy/helm/achilles/ -n licensing --create-namespace</code></pre>
`
            },
            {
                id: 'ch-ui-themes',
                number: '22',
                category: 'operations',
                title: 'Dizajnové Témy & Klávesové Ovládanie (F1-F12)',
                icon: '🎨',
                badge: 'Témy & UI',
                summary: 'Prehľad 4 grafických tém (Modern, DOS FoxPro, Cyberpunk 2077 HUD, Apple iOS s denným/nočným režimom) a 100% klávesové ovládanie bez myši.',
                targetView: 'view-config',
                targetViewName: 'Prepínač Tém',
                content: `
<h3>22.1 Štyri Profesionálne Dizajnové Témy</h3>
<p>
Achilles ponúka 4 jedinečné dizajnové prostredia prispôsobené preferenciám používateľa:
</p>
<ol>
    <li><strong>💼 Modern Enterprise:</strong> Čisté, responzívne webové rozhranie v štýle moderných cloudových platforiem s tmavým bridlicovým pozadím a azúrovými akcentmi.</li>
    <li><strong>💾 DOS (FoxPro 2.6 TUI):</strong> Autentická retro textová estetika DOS/FoxPro s kaskádovými oknami, pravouhlými tieňmi, CRT phosphor filtrom a žltými akcelerátormi.</li>
    <li><strong>⚡ Cyberpunk 2077 HUD:</strong> Futuristické neónové rozhranie v štýle Night City so žiarivým azúrovým (<code>#00f0ff</code>), purpurovým (<code>#ff0055</code>) a neónovo žltým akcentom s kybernetickými prvkami.</li>
    <li><strong>🍎 Apple iOS Glassmorphism:</strong> Jemná elegancia podľa Apple Human Interface Guidelines s polopriehľadným frosted glass efektom (<code>backdrop-filter: blur(25px)</code>) a trojrežimovým systémom:
        <ul>
            <li>🌓 <strong>Systémový (Auto):</strong> Automatická adaptácia na nastavenie operačného systému cez <code>prefers-color-scheme</code>.</li>
            <li>☀️ <strong>Denný (Light):</strong> Čistý iOS Grouped Background s bielymi kartami a systémovou modrou.</li>
            <li>🌙 <strong>Nočný (Dark):</strong> Hlboký OLED True Black podklad so zvýrazneným kontrastom.</li>
        </ul>
    </li>
</ol>

<h3>22.2 Klávesové Skratky (100% Ovládateľné Bez Myši)</h3>
<table class="table" style="width: 100%; margin: 12px 0;">
    <thead>
        <tr><th>Kláves</th><th>Funkcia</th></tr>
    </thead>
    <tbody>
        <tr><td><code>F1</code></td><td>Otvorenie tejto Komplexnej Príručky & Nápovedy.</td></tr>
        <tr><td><code>F2</code></td><td>Prechod na obrazovku Prehľad (Overview).</td></tr>
        <tr><td><code>F3</code></td><td>Prechod na správu Licencií (Licenses).</td></tr>
        <tr><td><code>F4</code></td><td>Prechod na Podpisové Kľúče & PQC.</td></tr>
        <tr><td><code>F5</code></td><td>Prechod na Relay uzly.</td></tr>
        <tr><td><code>F6</code></td><td>Prechod na Kryptografický Auditný Denník.</td></tr>
        <tr><td><code>F7</code></td><td>Prechod na Air-Gap Portál.</td></tr>
        <tr><td><code>F8</code></td><td>Prechod na Webhooky.</td></tr>
        <tr><td><code>F9</code></td><td>Prechod na Diagnostiku & Súlad CRA.</td></tr>
        <tr><td><code>F10 / Alt</code></td><td>Aktivácia a zameranie hlavného menu.</td></tr>
        <tr><td><code>V</code></td><td>Okamžité otvorenie dialógu na vystavenie licencie.</td></tr>
        <tr><td><code>O / C</code></td><td>Nastavenie administrátorského API kľúča.</td></tr>
        <tr><td><code>/</code></td><td>Zameranie vyhľadávacieho poľa v tejto príručke.</td></tr>
        <tr><td><code>Esc</code></td><td>Zatvorenie modálneho okna alebo návrat z podmenu.</td></tr>
    </tbody>
</table>
`
            }
        ]
    };

    // Globálny stav pre sprievodcu
    let currentCategory = 'all';
    let searchQuery = '';
    let currentChapterId = 'ch-intro';

    /**
     * Inicializácia Príručky
     */
    function initHelpGuide() {
        const tocListEl = document.getElementById('help-toc-list');
        const filterPillsEl = document.getElementById('help-filter-pills');
        const searchInputEl = document.getElementById('help-search-input');

        if (!tocListEl || !filterPillsEl) return;

        // Vykreslenie kategórií / filtrovacích piluliek
        renderCategoryPills();

        // Vykreslenie zoznamu kapitol
        renderChapterList();

        // Vykreslenie predvolenej kapitoly
        renderChapterContent(currentChapterId);

        // Event listener pre vyhľadávanie
        if (searchInputEl && !searchInputEl.dataset.bound) {
            searchInputEl.dataset.bound = 'true';
            searchInputEl.addEventListener('input', (e) => {
                searchQuery = e.target.value.toLowerCase().trim();
                renderChapterList();
            });
        }

        // Globálna klávesová skratka F1
        window.addEventListener('keydown', (e) => {
            if (e.key === 'F1') {
                e.preventDefault();
                if (typeof window.switchToView === 'function') {
                    window.switchToView('view-help');
                } else if (typeof window.switchRetroView === 'function') {
                    window.switchRetroView('view-help');
                }
            }
        });
    }

    /**
     * Vykreslenie kategórií
     */
    function renderCategoryPills() {
        const container = document.getElementById('help-filter-pills');
        if (!container) return;

        container.innerHTML = helpGuideData.categories.map(cat => `
            <button class="help-pill ${cat.id === currentCategory ? 'active' : ''}" 
                    onclick="window.HelpGuide.setCategory('${cat.id}')">
                <span>${cat.icon}</span> ${cat.label}
            </button>
        `).join('');
    }

    /**
     * Vykreslenie zoznamu kapitol podľa zvolenej kategórie a vyhľadávania
     */
    function renderChapterList() {
        const listEl = document.getElementById('help-toc-list');
        if (!listEl) return;

        const filtered = helpGuideData.chapters.filter(ch => {
            const matchesCat = currentCategory === 'all' || ch.category === currentCategory;
            const matchesSearch = !searchQuery || 
                ch.title.toLowerCase().includes(searchQuery) ||
                ch.summary.toLowerCase().includes(searchQuery) ||
                ch.content.toLowerCase().includes(searchQuery) ||
                ch.number.includes(searchQuery);
            return matchesCat && matchesSearch;
        });

        if (filtered.length === 0) {
            listEl.innerHTML = `
                <div style="padding: 24px; text-align: center; color: var(--text-secondary); font-size: 13px;">
                    🔍 Žiadna kapitola nevyhovuje výrazu "<strong>${escapeHtml(searchQuery)}</strong>".<br>
                    <button class="btn btn-secondary btn-sm" style="margin-top: 10px;" onclick="window.HelpGuide.resetFilters()">Zrušiť filter</button>
                </div>
            `;
            return;
        }

        listEl.innerHTML = filtered.map(ch => `
            <li class="help-toc-item ${ch.id === currentChapterId ? 'active' : ''}" 
                onclick="window.HelpGuide.selectChapter('${ch.id}')">
                <div class="help-toc-badge">${ch.number}</div>
                <div class="help-toc-text">
                    <div class="help-toc-title">${ch.icon} ${ch.title}</div>
                    <div class="help-toc-desc">${ch.summary}</div>
                </div>
            </li>
        `).join('');
    }

    /**
     * Vykreslenie obsahu konkrétnej kapitoly
     */
    function renderChapterContent(chapterId) {
        const chapter = helpGuideData.chapters.find(c => c.id === chapterId) || helpGuideData.chapters[0];
        currentChapterId = chapter.id;

        const contentEl = document.getElementById('help-content-display');
        if (!contentEl) return;

        contentEl.innerHTML = `
            <article class="help-article">
                <header class="help-article-header">
                    <div style="display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 10px; margin-bottom: 8px;">
                        <span class="badge" style="background: var(--accent-primary); color: #fff; font-size: 11px; padding: 4px 10px;">
                            Kapitola ${chapter.number} · ${chapter.badge}
                        </span>
                        ${chapter.targetView ? `
                            <button class="btn btn-secondary btn-sm" onclick="switchToView('${chapter.targetView}')" title="Otvoriť obrazovku v aplikácii">
                                ↗ Otvoriť: ${chapter.targetViewName}
                            </button>
                        ` : ''}
                    </div>
                    <h2 class="help-article-title">${chapter.icon} ${chapter.title}</h2>
                    <p class="help-article-lead">${chapter.summary}</p>
                </header>
                <div class="help-article-body">
                    ${chapter.content}
                </div>
                <footer class="help-article-footer" style="margin-top: 32px; padding-top: 20px; border-top: 1px solid var(--border-color); display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 12px;">
                    <div style="font-size: 12px; color: var(--text-secondary);">
                        📖 Achilles Enterprise Floating License Server · Oficiálna Príručka
                    </div>
                    <div style="display: flex; gap: 8px;">
                        <button class="btn btn-secondary btn-sm" onclick="window.HelpGuide.navigate(-1)">← Predchádzajúca</button>
                        <button class="btn btn-primary btn-sm" onclick="window.HelpGuide.navigate(1)">Ďalšia →</button>
                    </div>
                </footer>
            </article>
        `;

        // Prepojenie tlačidiel na kopírovanie kódu v pre blokoch
        contentEl.querySelectorAll('pre').forEach(pre => {
            if (!pre.querySelector('.btn-copy-code')) {
                const btn = document.createElement('button');
                btn.className = 'btn-copy-code';
                btn.textContent = 'Kopírovať';
                btn.onclick = function() {
                    const code = pre.querySelector('code')?.innerText || pre.innerText;
                    navigator.clipboard.writeText(code).then(() => {
                        btn.textContent = '✓ Skopírované';
                        btn.style.background = '#10b981';
                        btn.style.color = '#fff';
                        setTimeout(() => {
                            btn.textContent = 'Kopírovať';
                            btn.style.background = '';
                            btn.style.color = '';
                        }, 2000);
                    });
                };
                pre.style.position = 'relative';
                pre.appendChild(btn);
            }
        });

        // Posun na vrch obsahu
        contentEl.scrollTop = 0;
    }

    /**
     * Pomocná funkcia pre escapovanie HTML
     */
    function escapeHtml(str) {
        if (!str) return '';
        return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    // Verejné API
    window.HelpGuide = {
        init: initHelpGuide,
        setCategory: function (catId) {
            currentCategory = catId;
            renderCategoryPills();
            renderChapterList();
        },
        selectChapter: function (chapterId) {
            renderChapterContent(chapterId);
            renderChapterList();
        },
        resetFilters: function () {
            currentCategory = 'all';
            searchQuery = '';
            const input = document.getElementById('help-search-input');
            if (input) input.value = '';
            renderCategoryPills();
            renderChapterList();
        },
        navigate: function (direction) {
            const idx = helpGuideData.chapters.findIndex(c => c.id === currentChapterId);
            if (idx === -1) return;
            const nextIdx = (idx + direction + helpGuideData.chapters.length) % helpGuideData.chapters.length;
            const nextChapter = helpGuideData.chapters[nextIdx];
            if (nextChapter) {
                window.HelpGuide.selectChapter(nextChapter.id);
            }
        }
    };

    // Inicializácia pri načítaní DOM
    document.addEventListener('DOMContentLoaded', () => {
        setTimeout(initHelpGuide, 50);
    });
})();
