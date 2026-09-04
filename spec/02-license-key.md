# 2. License Key

> [← Artefakty](01-artefakty.md) · [Obsah](README.md) · [`symlic/1` →](03-symlic-1.md)

License Key je **nepriehľadný identifikátor**. Nenesie žiadne dáta a nedá sa overiť
offline bez licenčného súboru alebo servera.

> **Zdôvodnenie (nenormatívne).** Odpojenie dĺžky kľúča od veľkosti podpisu je jediný
> spôsob, ako mať súčasne post-kvantové podpisy a kľúč, ktorý sa dá prepísať z papiera
> alebo nadiktovať po telefóne.

## 2.1 Formát

```
SYM-XXXXX-XXXXX-XXXXX-XXXXX-CCCCC
    └───── 20 znakov = 100 bitov ─────┘└─ CRC-32C(20 znakov)
                                          skrátený na 25 bitov
```

**KEY-1.** Kľúč MUSÍ mať tvar `{PREFIX}-{G1}-{G2}-{G3}-{G4}-{G5}`, kde `G1`–`G5` sú
skupiny po 5 znakoch abecedy podľa `KEY-4`.

**KEY-2.** Skupiny `G1`–`G4` (20 znakov) MUSIA niesť **100 bitov** z kryptograficky
bezpečného generátora náhodných čísel (CSPRNG).

**KEY-3.** Skupina `G5` (5 znakov) MUSÍ niesť kontrolný súčet **CRC-32C** (Castagnoli)
nad 20 znakmi `G1`–`G4` v ich normalizovanom tvare (`KEY-6`), skrátený na 25
najvýznamnejších bitov a zakódovaný do 5 znakov Crockford Base32.

> ⚠️ **Otvorená nezrovnalosť v zadaní.** Kapitola 1.4 uvádza „128-bit náhodný",
> tabuľka v 5.1 „5 skupín po 5 znakov + 2 kontrolné znaky", a schéma v 5.1
> „100 bitov entropie / CRC-32C(20 znakov), 25 bitov" — pričom jej ASCII art má
> len 4 skupiny, zatiaľ čo priložený príklad má 5. Tieto tri údaje sa navzájom
> vylučujú.
>
> Táto špecifikácia preberá **najkonkrétnejší z nich** (100 bitov + 25-bitový CRC
> v 25 znakoch), pretože ako jediný sedí s uvedeným príkladom aj s dĺžkou
> Crockford Base32. **Rozhodni pred vydaním prvej licencie** — formát kľúča sa
> po ňom nedá zmeniť.
>
> Ak je zámer 128 bitov entropie, kľúč MUSÍ mať 26 znakov entropie + CRC, teda inú
> dĺžku skupín. 100 bitov je pre nepriehľadný identifikátor bezpečnostne
> dostatočných; rozdiel je ergonomický, nie kryptografický.

**KEY-4.** Abeceda MUSÍ byť **Crockford Base32**: `0123456789ABCDEFGHJKMNPQRSTVWXYZ`.

**KEY-5.** Prefix MUSÍ byť konfigurovateľný na úrovni vydavateľa (tenant). Default je
`SYM`. Prefix MUSÍ obsahovať 2–8 znakov z abecedy podľa `KEY-4`.

> *Poznámka:* prefix existuje preto, aby používateľ na prvý pohľad vedel, kam kľúč
> patrí — napr. `ACME-4K7QT-…`. Nemá bezpečnostnú funkciu.

**Príklad:**

```
SYM-4K7QT-9M2XA-B8ZH3-P6RWY-C1J8N
```

## 2.2 Normalizácia vstupu

**KEY-6.** Overovateľ MUSÍ pred spracovaním vstup normalizovať v tomto poradí:

1. odstrániť biele znaky a pomlčky,
2. previesť na veľké písmená,
3. nahradiť `I` → `1`, `L` → `1`, `O` → `0`.

**KEY-7.** Porovnávanie kľúčov MUSÍ prebiehať výlučne nad normalizovaným tvarom.
Vstup NESMIE byť odmietnutý len preto, že je zapísaný malými písmenami alebo bez
pomlčiek.

> **Zdôvodnenie (nenormatívne).** Crockford Base32 odstraňuje zámenu `I`/`L`/`1`
> a `O`/`0` — teda presne tie znaky, ktoré si ľudia pri prepisovaní mýlia.

**KEY-8.** Overovateľ MUSÍ overiť kontrolný súčet podľa `KEY-3` **pred** akýmkoľvek
sieťovým volaním a pri nezhode MUSÍ vrátiť lokálnu chybu „preklep v kľúči". Táto
kontrola NESMIE byť interpretovaná ako dôkaz platnosti licencie.

> *Poznámka:* normalizácia podľa `KEY-6` prebieha **pred** výpočtom CRC. Kľúč zapísaný
> ako `sym-4k7qt-…` a `SYM-4K7QT-…` preto dáva rovnaký kontrolný súčet.

## 2.3 Uloženie na strane vydavateľa

**KEY-9.** Vydavateľ NESMIE ukladať kľúč v otvorenej podobe.

**KEY-10.** Vydavateľ MUSÍ ukladať:

| Stĺpec | Výpočet | Účel |
|---|---|---|
| `key_hash` | `Argon2id(key, tenant_pepper)` | overenie predloženého kľúča |
| `key_lookup` | `HMAC-SHA256(tenant_pepper, key)[0..8]` | indexovateľné vyhľadanie |

**KEY-11.** `tenant_pepper` MUSÍ byť uložený mimo databázy (KMS, premenná prostredia
alebo súbor mimo zálohovaného zväzku).

> **Zdôvodnenie (nenormatívne).** Dump databázy tak nedáva použiteľné kľúče. `key_lookup`
> existuje preto, že Argon2id sa nedá indexovať — bez neho by overenie kľúča vyžadovalo
> lineárny prechod tabuľkou.

**KEY-12.** Kolízia `key_lookup` NESMIE byť považovaná za zhodu. Implementácia MUSÍ
po vyhľadaní podľa `key_lookup` overiť `key_hash`.

## 2.4 Čo License Key nerobí

**KEY-13.** License Key NESMIE niesť entitlements, limity, dátumy platnosti ani
akékoľvek iné dáta. Overovateľ NESMIE odvodzovať oprávnenia z obsahu kľúča.

**KEY-14.** Kľúč sa NESMIE dať overiť offline. Overenie oprávnení VYŽADUJE
[License File](03-symlic-1.md) alebo volanie servera.

> *Poznámka:* schémy typu *partial key verification* (čiastočný podpis zabudovaný
> v produktovom kľúči) sú v tejto špecifikácii zámerne nepodporené — jeden keygen
> zruší celý produktový rad a verejný kľúč sa nedá rotovať.

---

> [← Artefakty](01-artefakty.md) · [Obsah](README.md) · [`symlic/1` →](03-symlic-1.md)
