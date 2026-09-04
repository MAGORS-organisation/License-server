# 3. Rozsah projektu

> Časť zadania **Symbolon — licenčný server na .NET 10**. Späť na [obsah](README.md) · [prehľad projektu](../README.md).

## 3.1 Fázy

| Fáza | Obsah | Odhad |
|---|---|---|
| **V0.1 — Klin** | Formát licencie (špec + `Symbolon.Format`), krypto vrstva (hybrid ES256+ML-DSA-65), `symbolon-relay` (SQLite, jeden binár), `Symbolon.Client` SDK (.NET), CLI na generovanie kľúčov a licencií, floating checkout/renew/release/heartbeat, offline grant, dokumentácia | 45–60 čd |
| **V1.0 — Control plane** | Multi-tenant control plane (Postgres/EF Core 10), Admin API, politiky, entitlements, node-lock aktivácie, revokácie, Delegated Seat Grants, borrow/roaming, audit ledger, reporting API, OpenAPI 3.1, Helm chart | +90–120 čd |
| **V1.5 — Prevádzkovateľnosť** | Admin web konzola (Blazor), SSO/OIDC, webhooky, metriky/Grafana dashboard, migračné nástroje z FlexNet/Keygen | +60–80 čd |
| **V2.0 — Distribúcia** | Podpísaná distribúcia artefaktov (OCI), SBOM/CRA modul, transparency log | +60 čd |

## 3.2 MoSCoW pre V1.0

**Must:** floating (concurrent) licencie s lease/heartbeat; node-locked aktivácie; perpetual / subscription / trial; feature entitlements; offline licenčný súbor; air-gapped aktivácia; revokačný zoznam; relay s delegovanými grantmi; audit ledger; hybridné podpisy; .NET SDK; CLI; OpenAPI.

**Should:** borrow/roaming; overage stratégie; rezervácie a zákazy podľa skupín (ekvivalent FlexNet options file); queueing na sedadlo; named-user licencie; reporting (peak, denials); webhooky.

**Could:** token/credit-based licencie; metered usage; multi-region control plane; Java/Python/Go SDK; transparency log.

**Won't (V1):** billing a platby; distribúcia artefaktov; DRM/obfuskácia klienta; hardvérové dongle; on-premise SSO federácia mimo OIDC.

## 3.3 Non-goals (explicitne)

1. **Nie sme anti-piracy nástroj.** Klientské vynucovanie je odstrašenie a viditeľnosť využitia, nie prevencia. Kto chce binárku patchnúť, patchne ju. Toto povieme nahlas v dokumentácii — je to dôveryhodnejšie než sľuby, ktoré konkurencia nedodrží.
2. **Nie sme obfuskátor ani packer.** Neintegrujeme a neodporúčame binary protection ako bezpečnostnú vlastnosť.
3. **Nie sme fakturačný systém.** Integrujeme sa (webhooky), nenahrádzame.
4. **Nerobíme parity s FlexNet.** Options file syntax, `lmgrd` protokol a spätná kompatibilita s `.lic` súbormi sú explicitne mimo rozsahu.

---

[← 2. Kontext, problém a ciele](02-kontext-a-ciele.md) · [Obsah](README.md) · [4. Doménový model a licenčné modely →](04-domenovy-model.md)
