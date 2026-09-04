# 14. Otvorené otázky — rozhodnutia, ktoré musíš urobiť ty

> Časť zadania **Symbolon — licenčný server na .NET 10**. Späť na [obsah](README.md) · [prehľad projektu](../README.md).

| # | Otázka | Moje odporúčanie | Prečo to nemôžem rozhodnúť za teba |
|---|---|---|---|
| Q1 | Ideš do M0 validácie, alebo staviaš rovno? | **Choď do M0.** 7 dní vs. 70. | Závisí od toho, či je cieľ produkt alebo portfólio. Pri portfóliu M0 preskoč a stavaj hneď. |
| Q2 | Meno projektu | `Symbolon` (gr. σύμβολον — token rozlomený na dve polovice, ktoré sa musia zhodovať; presná metafora licencovania). Zálohy: `Tessera`, `Concord`. | Značka je tvoja; over EUIPO + `.dev` doménu + NuGet prefix. Pozor: „Symbolon" existuje ako názov tarotovej sady. |
| Q3 | AGPL server, alebo Apache celý? | AGPL + CLA — inak nemáš monetizačnú páku. | Ak je cieľ čisto portfólio, Apache-2.0 na všetko je jednoduchšie a priateľskejšie. |
| Q4 | Chceš to viazať na PRAESTAR alebo MATPEX? | MATPEX (s.r.o. je zmluvne použiteľnejšia pri komerčnej licencii a CLA). | Daňové a štruktúrne dôsledky, najmä vzhľadom na plán relokácie. |
| Q5 | `seatUnit` default pre prvé licencie | `machine` | Je to cenový model, nie technická voľba. |
| Q6 | Cieľ V0.1: standalone relay, alebo aj kompatibilita s Keygen API? | **Standalone.** Kompatibilita s cudzím API je záväzok, ktorý nevieš uniesť. | Ak z M0 vyjde, že ISV firmy už majú Keygen a chcú len on-prem floating pred ním, kompatibilita sa stáva klinom. To je jediný scenár, keď má zmysel. |
| Q7 | Máš vlastný/klientsky softvér, ktorý to reálne použije? | Ak áno, začni ním — prvý používateľ musíš byť ty. | Nevidím do tvojho klientskeho portfólia. |

---

[← 13. Roadmapa, odhad úsilia a riziká](13-roadmapa-a-rizika.md) · [Obsah](README.md) · [15. Zdroje →](15-zdroje.md)
