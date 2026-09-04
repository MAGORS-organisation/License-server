# 9. Bezpečnosť

> Časť zadania **Symbolon — licenčný server na .NET 10**. Späť na [obsah](README.md) · [prehľad projektu](../README.md).

## 9.1 Čo vlastne chránime (a čo nie)

| Aktívum | Hrozba | Dopad | Chránime? |
|---|---|---|---|
| Privátne podpisové kľúče vydavateľa | Kompromitácia → falšovanie ľubovoľnej licencie | **Katastrofický** — zabíja produkt aj u všetkých zákazníkov | ✅ Najvyššia priorita |
| Integrita licenčného súboru | Falšovanie entitlements | Vysoký | ✅ |
| Korektnosť počtu sedadiel | Pretečenie limitu | Stredný (únik tržieb) / vysoký (zmluvný spor) | ✅ |
| Dostupnosť licenčného servera | DoS → zákazník nemôže pracovať | Vysoký (reputačný) | ✅ |
| Dôvernosť auditných dát | Únik údajov o zamestnancoch zákazníka | Vysoký (GDPR) | ✅ |
| **Binárka aplikácie ISV** | Patchnutie kontroly licencie | Stredný | ❌ **Vedome nie** |

Posledný riadok je zámerný a povieme ho v README. Keygen to formuluje presne: *„all applications installed on an end-user's device are susceptible to cracking… an application is simply made up of bits and bytes… and those can always be modified."* Klientské vynucovanie je nástroj pre **čestné firmy, ktoré sa boja auditu**, nie proti crackerom. Kto sľubuje opak, klame.

## 9.2 Threat model (STRIDE, skrátený)

| # | Hrozba | Kategória | Zmiernenie |
|---|---|---|---|
| T1 | Falšovanie licenčného súboru | Tampering | Hybridný podpis; `crit`; fatal na zlý podpis |
| T2 | **Stripping PQ podpisu** | Tampering | `requiredAlgs` vnútri payloadu + normatívne pravidlá 9.4 |
| T3 | Replay starého licenčného súboru (s vyššími limitmi) | Tampering | `jti` + high-water-mark `iat` perzistovaný klientom |
| T4 | Replay lease tokenu na inom stroji | Spoofing | `fp` claim + serverová kontrola `holder_fp`; TTL v minútach |
| T5 | Replay `renew` požiadavky | Tampering | Monotónny `lease_seq`, server odmietne `seq ≤ posledné` |
| T6 | Rollback systémových hodín | Tampering | Server-authoritative čas; klient drží monotónny high-water mark; grace viazaný na `iat` v tokene |
| T7 | Klonovanie VM / kontajnera → duplicitný fingerprint | Spoofing | Detekcia kontajnera → random UUID; `machineUniqueness`; heartbeat culling odhalí duplicitu (dva heartbeaty z rovnakého fp z rôznych IP) |
| T8 | Kompromitovaný relay vydáva neobmedzené sedadlá | Elevation | Relay je obmedzený **podpísaným grantom**; nemôže vydať viac než `seats` z grantu; control plane kontroluje `usageDigest` |
| T9 | Relay zamlčuje využitie | Repudiation | Hash-chain `usageDigest` je podmienkou vydania ďalšieho grantu |
| T10 | Krádež licenčného kľúča z DB | Info disclosure | Argon2id + pepper; `key_lookup` je HMAC, nie prefix kľúča |
| T11 | Enumerácia licenčných kľúčov | Info disclosure | Konštantná odpoveď pre neexistujúcu aj neaktívnu licenciu; rate limiting per IP a per key prefix |
| T12 | DoS na checkout | DoS | Rate limiting; fronta s `maxWait`; `SKIP LOCKED` → žiadna globálna kontencia |
| T13 | Kompromitácia podpisového kľúča | Elevation | Hierarchia kľúčov, rotácia, revokácia `kid` v `.symrl`, krátke TTL súborov |
| T14 | Supply chain (kompromitovaný NuGet balík SDK) | Tampering | Reprodukovateľný build, Sigstore podpis, SBOM, pinnované závislosti |
| T15 | Odpočúvanie relay↔control plane | Info disclosure | mTLS s privátnou CA (`CustomRootTrust`) |

## 9.3 Hierarchia kľúčov a rotácia

```
Root Key Pair                 offline, HSM/air-gapped, ML-DSA-87 + ECDSA P-384
   │                          používa sa RAZ ročne na podpis Product kľúčov
   ├── Product Signing Keys   ML-DSA-65 + ECDSA P-256, platnosť 2 roky, prekryv 3 mes.
   │      │                   podpisujú .symlic, .symgrant, .symrl
   │      └── Lease Keys      ECDSA P-256, platnosť 1 mesiac, prekryv 1 deň
   │                          podpisujú lease tokeny; kľúč je DORUČENÝ v .symgrant
   └── Relay Identity Keys    ECDSA P-256 (mTLS klientsky cert), platnosť 1 rok
```

Pravidlá:
- **Aplikácia ISV má zapečený iba Root verejný kľúč** + JWKS Product kľúčov. Product kľúče sa dajú vymeniť bez novej verzie binárky (podpísané Rootom, distribuované s licenčným súborom).
- Rotácia je **plánovaná udalosť s prekryvom**, nie reakcia na incident. Ak rotáciu nikdy neskúšaš, nefunguje.
- `kid` konvencia: `{role}-{tenant}-{yyyy-MM}-{alg}`, napr. `prd-acme-2026-09-pq`.
- **Kompromitácia Product kľúča:** publikuj `.symrl` s revokáciou `kid` → klienti odmietnu všetko podpísané tým `kid`. Preto musí byť `revocation.maxAge` v politike krátky (default 7 dní) — inak je revokácia teoretická.
- Úložisko: v poradí preferencie (1) cloud KMS/HSM tam, kde vie PQC (v 2026 zriedkavé), (2) `MLDsaOpenSsl`/`MLDsaCng` nad providerom, (3) zašifrovaný PKCS#8 (`ExportEncryptedPkcs8PrivateKey`) s kľúčom z KMS/env, (4) plaintext PKCS#8 iba v dev. Konfigurácia musí **odmietnuť štart** v produkcii pri (4).

## 9.4 Obrana proti downgrade útoku na hybridný podpis (normatívne)

Toto je jediná reálna slabina nekompozitného hybridu a musí byť implementovaná presne:

1. `symlic.requiredAlgs` je **súčasťou podpísaného payloadu**. Útočník ho nevie zmeniť.
2. Ak verifikátor **podporuje** algoritmus z `requiredAlgs`, ale zodpovedajúci podpis v dokumente **chýba alebo neplatí** → **odmietnuť**. Bez výnimiek.
3. Ak verifikátor algoritmus **nepodporuje vôbec** (napr. macOS bez PQC a bez fallbacku):
   - `Strictness.Strict` → odmietnuť vždy;
   - `Strictness.Auto` (default) → odmietnuť, ak `exp − iat > 1 rok`; inak akceptovať a **zalogovať `degraded-verification`**;
   - `Strictness.Lenient` → akceptovať, logovať. Určené výlučne pre migračné okná.
4. Verifikátor **musí** hlásiť do telemetrie počet degradovaných overení. Ak sa to nemeria, nikto nezistí, že celá flotila roky beží iba na ES256.
5. Klientske SDK **musí** mať managed PQC fallback (BouncyCastle, verify-only), aby bol bod 3 v praxi zriedkavý.

## 9.5 PQC migračná stratégia

| Fáza | Obdobie | Stav |
|---|---|---|
| **P0** | dnes | Hybrid `ES256 + ML-DSA-65` je default pre všetky dlhoveké artefakty. `Strictness.Auto`. |
| **P1** | pri ≥ 95 % klientov s PQC podporou (merané telemetriou) | Prepnutie defaultu na `Strictness.Strict` pre nové licencie |
| **P2** | .NET 11+ / macOS PQC / OpenSSL 3.5 plošne | `requiredAlgs = ["ML-DSA-65"]` ako voliteľný profil `pq-only-v1` |
| **P3** | pri kryptoanalytickom oslabení ML-DSA | Pridanie tretieho podpisu `SLH-DSA-SHA2-128s` (hash-based, iné matematické predpoklady). Formát to už umožňuje bez zmeny. |

**Prečo tento plán obstojí pred auditom:** nespoliehame sa na jeden lattice-based predpoklad. Poľom `signatures` vieme kedykoľvek pridať algoritmus s iným bezpečnostným základom, bez zmeny formátu a bez rozbitia starých klientov. To je definícia crypto-agility a je to argument, ktorý sa dá napísať do bezpečnostnej dokumentácie pre zákazníka.

**Regulačný kontext (fakticky, bez marketingu):** EÚ roadmapa (NIS Cooperation Group, 23. 6. 2025) — začiatok prechodu do konca 2026, kritická infraštruktúra do konca 2030. FIPS 204 (ML-DSA) je finálny štandard. RFC 9964 (ML-DSA for JOSE and COSE) je Proposed Standard z mája 2026. Nič z toho **nenariaďuje** licenčným serverom PQC. Je to predstih, nie súlad — a takto to treba aj predávať, inak si vyrobíš dôveryhodnostný problém.

## 9.6 Prevádzková bezpečnosť API

- **mTLS pre `/relay/v1`**: `ClientCertificateMode.RequireCertificate` + `ChainTrustValidationMode.CustomRootTrust` s vlastným trust store (nie systémové CA). Pozor: certifikátové požiadavky sa **nedajú scopovať per-path** (TLS handshake predchádza routingu) → relay endpointy musia bežať na **samostatnom porte alebo SNI hoste**. Toto je architektonické obmedzenie Kestrelu, nie voľba.
- **Rate limiting** (`AddRateLimiter`): pásma `client` (sliding window, per licenčný kľúč + IP), `activation` (token bucket, prísnejšie), `admin` (per token). Vyčerpanie vracia `429` s `Retry-After`.
- **Odpovede neúnikajú existenciu**: neexistujúci kľúč, revokovaná licencia a expirovaná licencia vracajú rozdielne odpovede **až po overení držby** (t.j. po úspešnom checkout pokuse), nie pri lookupe.
- **Bez cookies, bez sessions.** Admin API len bearer JWT (OIDC discovery); WebAuthn/passkeys pre konzolu vo V1.5 (`SignInManager.PasskeySignInAsync` — endpointy si musíme napísať sami, ASP.NET Core Identity 10 dáva len stavebné bloky).
- Bezpečnostné hlavičky, HSTS, `SuppressDiagnosticsCallback` pre očakávané chyby, aby audit log nezaplavili 409-tky.

## 9.7 Supply chain a CRA

Toto je oblasť, kde má projekt **nadpriemernú návratnosť na vynaložený čas**, lebo je to zároveň marketing:

- **Reprodukovateľné buildy**: `ContinuousIntegrationBuild=true`, `Deterministic=true`, pinnované SDK cez `global.json`, Central Package Management, `packages.lock.json` s `RestoreLockedMode`.
- **SBOM** v CycloneDX pri každom release, priložený k GitHub Release aj k NuGet balíku.
- **Sigstore/cosign** podpis kontajnerov a `NuGet` balíkov; **SLSA build level 3** cieľ (GitHub Actions s `id-token: write`, reusable workflow, žiadne self-hosted runnery pre release).
- **Vulnerability disclosure policy** (`SECURITY.md`, 90-dňové okno, GitHub Security Advisories).
- **CRA relevancia**: povinnosti hlásenia aktívne zneužívaných zraniteľností platia od **11. 9. 2026**, plné požiadavky od **11. 12. 2027**. Ako open-source projekt s komerčnou ponukou budeš pravdepodobne v role **výrobcu** pre komerčnú edíciu. *(Presné dátumy a vlastnú rolu si over v oficiálnom texte nariadenia — sekundárne zdroje sa v detailoch rozchádzajú. Toto nie je právne stanovisko.)*

## 9.8 GDPR a údaje o zamestnancoch zákazníka

Licenčný server zbiera hostname a používateľské meno — a hostname býva `jan-novak-notebook`. To je **osobný údaj** a v Nemecku navyše téma pre podnikovú radu (BetrVG) skôr, než sa systém vôbec nasadí.

Návrh (default-safe):
- **Pseudonymizácia pri zbere**: SDK posiela `HMAC(tenant_salt, hostname)`, nie hostname. Mapovanie zostáva u zákazníka.
- Identifikovateľný reporting je **opt-in** na úrovni relayu a je viditeľne označený v UI aj v audite.
- Retencia auditu konfigurovateľná, default 400 dní, s automatickým prunovaním; export a výmaz na požiadanie.
- Dokumentácia obsahuje vzorový záznam o spracovateľských činnostiach a LIA (posúdenie oprávneného záujmu) — pre zákazníkov je to reálna hodnota, ktorú konkurencia nedodáva.

**Toto je predajný argument, nie len compliance.** Self-hosted + pseudonymizované by default + žiadny cloud tretej strany je presne to, čo chce európsky enterprise zákazník počuť, keď mu FlexNet loguje každé spustenie aplikácie s menom používateľa.

## 9.9 Čo je divadlo a nebudeme to robiť

| Praktika | Prečo nie |
|---|---|
| Obfuskácia / packing SDK | Jeden patch. Vytvára falošný pocit bezpečia a komplikuje debugovanie zákazníkom |
| „Phone home alebo zomri" bez grace | Poškodzuje čestných zákazníkov pri výpadku siete; crackerov nezastaví |
| Detekcia debuggera / VM ako blokovanie | Blokuje legitímne virtualizované prostredia (t.j. väčšinu enterprise) |
| Kontrola integrity vlastnej binárky | Kontroluje ju kód, ktorý útočník práve patchuje |
| Ukrývanie verejného kľúča | Kerckhoffs. Verejný kľúč je verejný |

---

[← 8. Referenčná implementácia (.NET 10)](08-referencna-implementacia.md) · [Obsah](README.md) · [10. Testovacia stratégia →](10-testovacia-strategia.md)
