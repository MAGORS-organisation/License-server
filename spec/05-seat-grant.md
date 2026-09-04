# 5. Seat Grant (`.symgrant`)

> [← Lease Token](04-lease-token.md) · [Obsah](README.md) · [Revocation List →](06-revocation-list.md)

Seat Grant je podpísané tvrdenie vydavateľa:

> *„Relay R smie vydať najviac N súbežných sedadiel pre licenciu L v okne [t0, t1],
> sekvencia S."*

Je to mechanizmus, ktorý umožňuje relayu fungovať **bez akéhokoľvek spojenia**
s control plane a bez distribuovaného konsenzu medzi relaymi.

---

## 5.1 Formát

**GNT-1.** Seat Grant MUSÍ byť JWS v General JSON Serialization s rovnakou
štruktúrou hlavičiek ako [License File](03-symlic-1.md#32-štruktúra-jws),
s `typ: symgrant+jws`.

**GNT-2.** Grant MUSÍ byť podpísaný hybridne (`ES256` + `ML-DSA-65`), rovnako ako
License File.

```jsonc
{
  "iss": "https://licenses.acme.example",
  "sub": "lic_01JQ8ZK4N9V2X6M0",
  "aud": "rly_01JQ8ZM2…",
  "jti": "gnt_01JQ8ZN7…",
  "iat": 1788480000, "nbf": 1788480000, "exp": 1788566400,   // 24 h
  "symgrant": {
    "v": 1,
    "seats": 10,
    "seatRange": [0, 9],
    "seq": 42,
    "supersedes": 41,
    "entitlements": ["core", "module.cad-export"],
    "leasePolicy": { "ttl": "PT10M", "graceTtl": "PT4H" },
    "leaseKey": { "kty": "EC", "crv": "P-256", "x": "…", "y": "…", "kid": "lease-rly1-2026-09" },
    "offlineExtension": { "allowed": true, "maxExtensions": 7 }
  }
}
```

---

## 5.2 Polia

| Pole | Povinnosť | Význam |
|---|---|---|
| `aud` | MUSÍ | ID relayu, pre ktorý je grant vydaný |
| `seats` | MUSÍ | počet sedadiel delegovaných tomuto relayu |
| `seatRange` | MUSÍ | inkluzívny interval `[od, do]` čísel sedadiel |
| `seq` | MUSÍ | monotónne per dvojicu *(licencia, relay)* |
| `supersedes` | MAL BY | `seq` grantu, ktorý tento nahrádza |
| `entitlements` | MUSÍ | oprávnenia, ktoré smie relay vkladať do lease tokenov |
| `leasePolicy` | MUSÍ | `ttl` a `graceTtl` pre vydávané leases |
| `leaseKey` | MUSÍ | verejný **a privátny** materiál podpisového kľúča pre lease tokeny |
| `offlineExtension` | VOLITEĽNÉ | povolenie a limit autonómneho predĺženia |

**GNT-3.** `seatRange` MUSÍ obsahovať presne `seats` čísel, teda
`seatRange[1] − seatRange[0] + 1 == seats`.

---

## 5.3 Disjunktnosť — hlavný invariant

**GNT-4.** Vydavateľ MUSÍ zabezpečiť, že súčet `seats` cez všetky **platné** granty
pre danú licenciu je menší alebo rovný `limits.maxSeats` z jej
[License File](03-symlic-1.md).

**GNT-5.** `seatRange` dvoch súčasne platných grantov pre tú istú licenciu sa
**NESMÚ prekrývať**.

> **Zdôvodnenie (nenormatívne).** `GNT-4` a `GNT-5` sú dôvod, prečo systém nepotrebuje
> kvórum ani komunikáciu medzi relaymi. Dva relaye po 12 sedadiel z 25 fungujú
> nezávisle; keď jeden padne, druhý ďalej obsluhuje svojich 12. Explicitný `seatRange`
> (namiesto samotného počtu) robí prekryv detekovateľným pri audite.

**GNT-6.** Grant je platný, ak `nbf ≤ now ≤ exp`, jeho podpisy sú platné, `seq`
je najvyššia videná pre danú dvojicu *(licencia, relay)*, a nie je revokovaný.

---

## 5.4 Sekvencia a ochrana proti replay

**GNT-7.** Relay MUSÍ perzistovať `last_seq` pre každú licenciu a MUSÍ odmietnuť
grant so `seq` menším alebo rovným `last_seq`.

**GNT-8.** Vydavateľ MUSÍ perzistovať `last_seq` pre každú dvojicu
*(licencia, relay)* a MUSÍ ho zvýšiť pri každom vydaní grantu.

**GNT-9.** Ak grant obsahuje `supersedes`, relay MUSÍ okamžite prestať používať
grant s uvedenou `seq`, aj keď ešte nevypršal.

---

## 5.5 Správanie relayu

**GNT-10.** Relay NESMIE vydať lease token pre sedadlo mimo `seatRange` platného
grantu.

**GNT-11.** Relay NESMIE súbežne držať viac aktívnych leases než `seats`.

**GNT-12.** Po uplynutí `exp` grantu relay MUSÍ prestať vydávať **nové** leases.
Existujúce leases MUSIA dobehnúť do konca svojho `graceTtl`.

> **Zdôvodnenie (nenormatívne).** Toto je bezpečná degradácia: výpadok spojenia
> s control plane neukončí prebiehajúcu prácu, ale ani nedovolí kapacitu prekročiť.

**GNT-13.** `leaseKey` obsahuje privátny materiál. Relay ho MUSÍ uložiť šifrovane
a NESMIE ho zapísať do logu ani exportovať cez API.

**GNT-14.** Ak `offlineExtension.allowed` je `true`, relay MÔŽE predĺžiť platnosť
grantu najviac `maxExtensions`-krát o pôvodnú dĺžku okna `exp − nbf`, bez kontaktu
s control plane.

**GNT-15.** Relay MUSÍ počet vykonaných predĺžení perzistovať a zahrnúť ho do
najbližšieho hlásenia využitia.

---

## 5.6 Nasadzovacie topológie

Nenormatívny prehľad toho, čo grant umožňuje:

| # | Topológia | Poznámka |
|---|---|---|
| T1 | Len control plane | Žiadny relay, klienti volajú `/v1` priamo |
| T2 | Control plane + 1 relay | Relay drží celý `maxSeats` grant |
| T3 | Control plane + N relayov s disjunktnými grantmi | HA a/alebo geografické oddelenie závodov |
| T4 | Relay standalone s dlhodobým grantom | Air-gapped, grant cez USB |
| T5 | Len offline licenčné súbory | Žiadny floating |

**Cena za T3:** sedadlá **nie sú** dynamicky prerozdeliteľné medzi relaymi bez
kontaktu s control plane. Zmierňuje sa to krátkymi grantmi s automatickým
prebalansovaním.

---

> [← Lease Token](04-lease-token.md) · [Obsah](README.md) · [Revocation List →](06-revocation-list.md)
