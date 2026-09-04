# 13. Roadmapa, odhad úsilia a riziká

> Časť zadania **Symbolon — licenčný server na .NET 10**. Späť na [obsah](README.md) · [prehľad projektu](../README.md).

## 13.1 Odhad (jeden skúsený vývojár, čisté človekodni)

| Balík | ČD |
|---|---|
| Špecifikácia formátu + testovacie vektory | 5 |
| `Symbolon.Crypto` (hybrid, key ring, JWKS, fallback) | 9 |
| `Symbolon.Format` (JWS, PEM, verziovanie, verifikátor) | 7 |
| `Symbolon.Client` SDK (checkout, heartbeat, grace, fingerprint, offline cache) | 12 |
| `Symbolon.Relay` (SQLite, lease engine, AOT, sync) | 14 |
| CLI (`keys`, `license`, `grant`, `doctor`) | 6 |
| Testy V0.1 (unit + property + integračné + E2E) | 10 |
| Dokumentácia, README, quickstart, web | 7 |
| **V0.1 spolu** | **~70** *(rozsah 55–85 podľa toho, koľko času zožerie AOT a PQC platformová matica)* |
| Control plane: doména, EF Core, migrácie, Admin API | 30 |
| Politiky, entitlements, node-lock, revokácie | 14 |
| Delegated Seat Grants + relay sync + air-gapped | 16 |
| Borrow/roaming, fronta, rezervácie | 12 |
| Audit ledger + reporting API | 10 |
| Observabilita, health, Helm, deploy | 8 |
| Testy V1.0 | 20 |
| Dokumentácia V1.0 | 10 |
| **V1.0 spolu (kumulatívne)** | **~190** |

> Pôvodný odhad v kap. 1.0 (150–180) sa po rozpise ukázal ako mierne optimistický. To je normálne a je to dôvod, prečo sa rozpisuje. **Počítaj s 190 ČD k dôveryhodnej 1.0.** Pri 2 dňoch týždenne je to ~22 mesiacov. Pri 3 dňoch ~15 mesiacov. Toto číslo si pozri ešte raz, kým sa rozhodneš.

## 13.2 Míľniky

| Míľnik | Kritérium hotovosti |
|---|---|
| **M0 — Validácia** (7 dní) | 5 rozhovorov, rozhodnutie go/no-go |
| **M1 — Spike** (5 ČD) | ML-DSA podpis+overenie na Linux/Windows/macOS(fallback); AOT relay hello-world; `SKIP LOCKED` benchmark. **Ak M1 odhalí blokujúci problém s PQC platformami, prehodnoť ADR-003 pred zvyškom.** |
| **M2 — Formát zmrazený** | `spec/symlic-1.md` + testovacie vektory, verejné |
| **M3 — V0.1 relay** | 20 E2E scenárov zelených, quickstart do 10 min |
| **M4 — 90-dňové vyhodnotenie** | Kritériá z 1.3 |
| **M5 — V1.0** | Control plane, migračný nástroj z Keygenu, 2 referencie |

## 13.3 Register rizík

| # | Riziko | P | D | Zmiernenie |
|---|---|---|---|---|
| R1 | **Nikto to nepoužije** | Vysoká | Vysoký | M0 validácia; kill criteria; hodnota ako portfólio artefakt aj pri nule adopcie |
| R2 | Rozsah sa rozšíri na FlexNet parity | Vysoká | Vysoký | Non-goals; rozpočet ČD na balík; „nie" ako default |
| R3 | PQC platformová matica (macOS, staré Linuxy) zožerie viac času než plán | Stredná | Stredný | M1 spike ako prvý; fallback od začiatku; ADR-003 je revidovateľné |
| R4 | `[Experimental]` ML-DSA API sa v .NET 11 zmení | Stredná | Stredný | Izolované za `ISignatureProvider`; KAT vektory zachytia zmenu správania |
| R5 | AGPL odradí enterprise | Stredná | Stredný | SDK Apache-2.0; pripravená komerčná výnimka a FAQ |
| R6 | Chyba v účtovaní sedadiel u zákazníka | Nízka | **Kritický** | Invarianty v schéme; property/konkurenčné testy; mutation score ≥ 80 % |
| R7 | Kompromitácia podpisového kľúča | Nízka | **Kritický** | Hierarchia, offline root, revokácia `kid`, krátke TTL |
| R8 | Vyhorenie / projekt zomrie na 60 % | **Vysoká** | Vysoký | Fázovanie tak, aby V0.1 bola samostatne hodnotná; časový strop; kill criteria |
| R9 | Právne riziko značky „Symbolon" | Nízka | Nízky | Rešerš EUIPO + domény pred prvým commitom (30 min) |

**R8 je štatisticky najpravdepodobnejšia príčina smrti projektu**, nie technika. Preto je celý plán postavený tak, aby po ~70 ČD existoval hotový, publikovateľný, samostatne užitočný artefakt.

---

[← 12. OSS governance a monetizácia](12-oss-governance.md) · [Obsah](README.md) · [14. Otvorené otázky — rozhodnutia, ktoré musíš urobiť ty →](14-otvorene-otazky.md)
