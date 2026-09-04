# Licencovanie

Symbolon používa **rozdelené licencovanie** podľa [ADR-006](docs/06-architektura.md).
Tento súbor je záväzná mapa — pri rozpore s čímkoľvek iným v repozitári platí on.

**Držiteľ autorských práv:** Matej Langsfeld, 2026.

> ⚠️ **Otvorené: právny subjekt.** Otázka **Q4** v kapitole
> [14](docs/14-otvorene-otazky.md) (PRAESTAR vs. MATPEX s.r.o.) nie je rozhodnutá.
> Do jej rozhodnutia je držiteľom autorských práv fyzická osoba. Pri komerčnej
> dual-licencii a CLA je to subjekt, ktorý uzatvára zmluvy — treba to uzavrieť
> **skôr**, než príde prvý externý príspevok alebo prvý platený zákazník.

---

## Mapa licencií

| Cesta | Licencia | SPDX | Prečo |
|---|---|---|---|
| **koreň repozitára** (default) | GNU Affero General Public License v3.0 only | `AGPL-3.0-only` | Najreštriktívnejšia licencia ako bezpečný default. Všetko, čo nie je nižšie výslovne vyňaté, je AGPL. |
| `spec/` | Creative Commons Attribution 4.0 International | `CC-BY-4.0` | Špecifikácia formátu, protokol a testovacie vektory musia byť voľne implementovateľné kýmkoľvek — aj konkurenciou. Inak to nie je otvorený formát. |
| `src/Symbolon.Format/` | Apache License 2.0 | `Apache-2.0` | |
| `src/Symbolon.Crypto/` | Apache License 2.0 | `Apache-2.0` | |
| `src/Symbolon.Client/` | Apache License 2.0 | `Apache-2.0` | SDK sa **linkuje do produktov zákazníkov**. Copyleft je tam disqualifikátor — nikto by ho nepoužil. |
| `src/Symbolon.Protocol/` | Apache License 2.0 | `Apache-2.0` | Zdieľané DTO kontrakty server ↔ klient; kompilujú sa do klientskeho SDK. |
| `src/Symbolon.Domain/` | GNU AGPL v3.0 only | `AGPL-3.0-only` | |
| `src/Symbolon.Data/` | GNU AGPL v3.0 only | `AGPL-3.0-only` | |
| `src/Symbolon.ControlPlane/` | GNU AGPL v3.0 only | `AGPL-3.0-only` | |
| `src/Symbolon.Relay/` | GNU AGPL v3.0 only | `AGPL-3.0-only` | |
| `src/Symbolon.Cli/` | GNU AGPL v3.0 only | `AGPL-3.0-only` | |

Rozdelenie `src/` zodpovedá štruktúre riešenia v kapitole
[8.1](docs/08-referencna-implementacia.md).

### Zatiaľ nepriradené

| Cesta | Stav |
|---|---|
| `tests/` | Testy sa neredistribuujú. Riadia sa licenciou projektu, ktorý testujú (`Symbolon.Format.Tests` → Apache-2.0, `Symbolon.Domain.Tests` → AGPL). **Výnimka:** FIPS 204 KAT vektory a testovacie vektory formátu patria do `spec/` (CC BY 4.0), nie do `tests/`. |
| `deploy/` | Helm chart, docker-compose, systemd unit — nasadzujú AGPL server, takže AGPL-3.0-only. Ak sa má chart používať samostatne, zváž Apache-2.0. |
| `docs/` | **Interné.** Viď nižšie. |

### `docs/` — zadanie, nie špecifikácia

Adresár `docs/` obsahuje zadanie projektu a **nie je pokrytý CC BY 4.0**, hoci jeho
kapitoly 5 a 7 sú vecným základom budúcej špecifikácie. Dôvod: zadanie obsahuje aj
interný obchodný materiál — oportunitné náklady, kritériá zabitia projektu, otázku
právneho subjektu.

Pred zverejnením repozitára treba `docs/` rozdeliť:

- **verejné** → normatívne časti kapitol 5 (formát `symlic/1`), 7 (floating protokol)
  a testovacie vektory sa prepíšu do `spec/` pod CC BY 4.0,
- **interné** → kapitoly 1, 12, 13, 14 zostávajú v privátnom repozitári alebo sa
  odstránia.

Do vykonania tohto rozdelenia je `docs/` **All rights reserved**.

---

## Ako to označovať v kóde

Každý projekt v `src/` dostane pri vytvorení vlastný `LICENSE` súbor (kópiu
príslušného textu z `LICENSES/`) a v `.csproj`:

```xml
<PropertyGroup>
  <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
  <!-- alebo AGPL-3.0-only pre serverové projekty -->
  <Copyright>Copyright (c) 2026 Matej Langsfeld</Copyright>
</PropertyGroup>
```

Hlavička zdrojového súboru (`AGPL-3.0-only`, nie `-or-later` — verzia je zámerne
fixovaná, aby budúca verzia AGPL nezmenila podmienky bez rozhodnutia vlastníka):

```csharp
// SPDX-FileCopyrightText: 2026 Matej Langsfeld
// SPDX-License-Identifier: AGPL-3.0-only
```

---

## Texty licencií

| Súbor | Zdroj |
|---|---|
| [`LICENSE`](LICENSE) | AGPL-3.0-only — default pre celý repozitár |
| [`spec/LICENSE`](spec/LICENSE) | CC BY 4.0 — špecifikácia a testovacie vektory |
| [`LICENSES/AGPL-3.0-only.txt`](LICENSES/AGPL-3.0-only.txt) | referenčná kópia |
| [`LICENSES/Apache-2.0.txt`](LICENSES/Apache-2.0.txt) | referenčná kópia |
| [`LICENSES/CC-BY-4.0.txt`](LICENSES/CC-BY-4.0.txt) | referenčná kópia |

Všetky tri texty sú stiahnuté nezmenené z GitHub Licenses API
(`gh api licenses/{agpl-3.0,apache-2.0,cc-by-4.0}`), nie prepísané ručne.

**Poznámka k `AGPL-3.0` vs. `AGPL-3.0-only`:** text licencie je pre obe varianty
identický. Rozdiel je v poznámke o autorských právach v zdrojových súboroch —
tento projekt používa **`-only`**, teda bez klauzuly „alebo ktorákoľvek neskoršia
verzia".

---

## Prispievanie a CLA

ADR-006 vyžaduje **CLA v štýle Apache ICLA** pre príspevky do AGPL komponentov.
Bez neho nie je možná komerčná dual-licencia, ktorá je jedinou navrhovanou
monetizačnou cestou (kapitola [12.2](docs/12-oss-governance.md)).

> ⚠️ **CLA zatiaľ neexistuje.** Nie je súčasťou tohto commitu — je to právny
> dokument, ktorý má schváliť právnik, nie ho odvodiť zo zadania. Kým nie je na
> mieste, **neprijímaj externé príspevky do `src/`** — spätné doberanie súhlasov od
> prispievateľov je v praxi takmer nemožné a zablokuje dual-licenciu natrvalo.

---

## Prečo takto (zhrnutie argumentov z ADR-006)

- **SDK musí byť permisívne.** Linkuje sa do produktov ISV. Apache-2.0 vrátane
  patentovej klauzuly je štandard, ktorý prejde právnym oddelením.
- **Server musí byť OSI-schválený.** To je celý klin proti Keygenu, ktorý je
  Fair Source (FCL-1.0-ALv2), nie OSI. Pri BUSL/FCL/ELv2 stratíš jediný
  odlišujúci argument. Predávať *licenčný* nástroj pod non-OSI licenciou navyše
  okamžite vyvolá otázku „prečo si ho sami nedokážete otvoriť?".
- **AGPL + CLA necháva komerčnú dual-licenciu.** AGPL je zároveň dôvod, prečo
  enterprise právne oddelenie príde kúpiť výnimku. To je funkcia, nie chyba.
- **Riziko:** časť firiem má plošný zákaz AGPL a odíde bez rozhovoru. Zmierňuje to
  fakt, že SDK (ktoré sa dotýka ich kódu) je Apache-2.0 a server beží ako
  samostatný proces — ale počítaj s opakovanou otázkou a priprav si FAQ.
