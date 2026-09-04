# 12. OSS governance a monetizácia

> Časť zadania **Symbolon — licenčný server na .NET 10**. Späť na [obsah](README.md) · [prehľad projektu](../README.md).

## 12.1 Governance

| Prvok | Rozhodnutie |
|---|---|
| Licencie | Apache-2.0 (SDK/špec) / AGPL-3.0-only (server) / CC BY 4.0 (špecifikácia) |
| Príspevky | **CLA** (Apache ICLA štýl), nie iba DCO — CLA je nutná podmienka pre komerčnú dual-licenciu |
| Rozhodovanie | BDFL (ty) do 1.0; potom TSC, ak vznikne komunita |
| Zmeny protokolu | RFC proces v `spec/rfcs/`, min. 14 dní na komentáre |
| Vydania | SemVer; LTS vetva viazaná na .NET LTS; bezpečnostné opravy 24 mesiacov |
| CoC | Contributor Covenant 2.1 |
| Bezpečnosť | `SECURITY.md`, GitHub Security Advisories, 90-dňové okno |

## 12.2 Hranica open-core (ak sa vôbec dostaneš do monetizácie)

**Pravidlo:** open-core hranica ide cez **organizačnú škálu**, nikdy cez bezpečnosť. Ak dáš PQC, mTLS alebo audit do platenej edície, si presne ten dodávateľ, ktorého sám kritizuješ.

| Zdarma (AGPL) | Komerčné |
|---|---|
| Všetky licenčné modely vrátane floating | SSO/SAML, SCIM |
| Hybridné PQC podpisy | Multi-tenant izolácia a delegovaná správa |
| Relay, air-gapped, borrow | Vysoká dostupnosť s automatickým prebalansovaním grantov |
| Audit ledger, reporting API | Dlhodobá retencia + BI konektory |
| mTLS, rate limiting, celý threat model | Komerčná licencia (výnimka z AGPL) |
| Všetky SDK | SLA support, integračné služby |

## 12.3 Realistické očakávania od monetizácie

Buď k sebe úprimný v číslach. Porovnateľné projekty:
- Keygen: musel prejsť na non-OSI licenciu, aby ubránil tržby.
- Keygate: ~47★ po mesiacoch.
- keygen-relay (MIT, od etablovaného hráča): ~34★.

**Realistický scenár na 24 mesiacov:** 500–1 500 hviezd, 10–40 nasadení, 2–6 platiacich zákazníkov na komerčnej licencii/supporte v pásme 2–8 k€/rok. To je 10–40 k€ ročne — **nie je to biznis, je to príjemný doplnok k hlavnej činnosti a veľmi dobrý zdroj inbound leadov pre konzultáciu.**

Ak si od toho sľubuješ viac, je to práve to miesto, kde ťa mám upozorniť: **hodnota tohto projektu je primárne v pozicionovaní PRAESTAR/MATPEX, nie v priamych tržbách z produktu.** Ak s tým vieš žiť, projekt dáva zmysel. Ak od neho čakáš produktový biznis, čísla to nepodporujú.

---

[← 11. Prevádzka](11-prevadzka.md) · [Obsah](README.md) · [13. Roadmapa, odhad úsilia a riziká →](13-roadmapa-a-rizika.md)
