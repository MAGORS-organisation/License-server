# 6. Revocation List (`.symrl`)

> [← Seat Grant](05-seat-grant.md) · [Obsah](README.md) · [Floating protokol →](07-floating-protokol.md)

Revokačný zoznam je jediný spôsob, ako v teréne zneplatniť licenciu, stroj alebo
**kompromitovaný podpisový kľúč** bez vydania novej verzie binárky.

---

## 6.1 Formát

**RVL-1.** Revokačný zoznam MUSÍ byť JWS v General JSON Serialization s `typ: symrl+jws`.

**RVL-2.** Zoznam MUSÍ byť podpísaný hybridne (`ES256` + `ML-DSA-65`).

**RVL-3.** `exp` MUSÍ byť krátke. ODPORÚČA sa 24 hodín.

```jsonc
{
  "iss": "https://licenses.acme.example",
  "iat": 1788480000, "exp": 1788566400,
  "symrl": {
    "v": 1, "seq": 1187,
    "full": false, "since": 1186,
    "revoked": [
      { "t": "license", "id": "lic_01JQ…", "at": 1788470000, "reason": "non-payment" },
      { "t": "machine", "id": "mch_01JQ…", "at": 1788471000, "reason": "fraud" },
      { "t": "kid",     "id": "prd-acme-2025-ec", "at": 1788400000, "reason": "key-compromise" }
    ]
  }
}
```

---

## 6.2 Polia

| Pole | Povinnosť | Význam |
|---|---|---|
| `seq` | MUSÍ | monotónne rastúce číslo zoznamu |
| `full` | MUSÍ | `true` = úplný zoznam, `false` = delta |
| `since` | MUSÍ*, ak `full: false` | `seq`, od ktorej delta nadväzuje |
| `revoked` | MUSÍ | zoznam položiek; MÔŽE byť prázdny |

**RVL-4.** Položka MUSÍ obsahovať `t` (typ subjektu), `id` a `at` (čas revokácie).
`reason` je VOLITEĽNÉ.

**RVL-5.** `t` MUSÍ byť jedna z hodnôt: `license`, `machine`, `kid`, `relay`, `lease`.

**RVL-6.** Neznámu hodnotu `t` MUSÍ overovateľ **ignorovať**, nie odmietnuť celý zoznam.

> **Zdôvodnenie (nenormatívne).** Inak by pridanie nového typu subjektu zablokovalo
> revokáciu na všetkých starších klientoch — teda presne v situácii, keď ju potrebuješ
> najviac.

---

## 6.3 Spracovanie

**RVL-7.** Overovateľ MUSÍ perzistovať `last_seen_seq` a MUSÍ odmietnuť zoznam so
`seq` menším než uložená hodnota.

**RVL-8.** Ak `full` je `false`, overovateľ MUSÍ overiť, že `since` sa rovná jeho
`last_seen_seq`. Pri medzere MUSÍ vyžiadať úplný zoznam (`full: true`).

**RVL-9.** Revokácia je **trvalá**. Položka, ktorá raz bola v zozname, sa MUSÍ
považovať za revokovanú aj vtedy, keď v neskoršej delte nie je uvedená.

**RVL-10.** Overovateľ NEMAL BY dôverovať zoznamu staršiemu než
`symlic.policy.revocation.maxAge` z licenčného súboru (default `P7D`).

**RVL-11.** Ak overovateľ nemá žiadny zoznam alebo má len zastaraný, MUSÍ sa správať
podľa konfigurovanej striktnosti — rovnakej škály ako `LIC-25`. Default `auto`
znamená pokračovať a zalogovať `stale-revocation-list`.

> **Zdôvodnenie (nenormatívne).** Tvrdé odmietnutie pri chýbajúcom zozname by z výpadku
> siete urobilo výpadok zákazníkovej výroby. To je horší výsledok než krátke okno,
> v ktorom platí revokovaná licencia.

---

## 6.4 Revokácia podpisového kľúča

**RVL-12.** Revokácia s `t: "kid"` MUSÍ viesť k odmietnutiu **všetkých** artefaktov
podpísaných uvedeným `kid`, aj keď je ich podpis kryptograficky platný a ešte
nevypršali.

**RVL-13.** Vydavateľ MUSÍ pri revokácii `kid` vydať nový zoznam okamžite, nie až
v pravidelnom cykle.

**RVL-14.** Overovateľ NESMIE revokáciu `kid` aplikovať na zoznam samotný, ak by tým
znemožnil jeho overenie. Zoznam podpísaný revokovaným `kid` sa MUSÍ odmietnuť.

> **Zdôvodnenie (nenormatívne).** `RVL-12` je dôvod, prečo musí byť `maxAge` krátky.
> Pri `maxAge` v mesiacoch je revokácia kompromitovaného kľúča teoretická — flotila
> ju uvidí až po tom, čo útočník stihne vydať ľubovoľné množstvo falošných licencií.

---

## 6.5 Distribúcia

**RVL-15.** Zoznam MUSÍ byť dostupný na `GET /v1/revocations/latest?since={seq}`.

**RVL-16.** Server MUSÍ vrátiť deltu, ak `since` zodpovedá známej sekvencii; inak
MUSÍ vrátiť úplný zoznam.

**RVL-17.** Relay MUSÍ zoznam distribuovať pripojeným klientom a MUSÍ ho zahrnúť do
air-gapped výmeny podľa
[07-floating-protokol.md](07-floating-protokol.md#77-air-gapped-tok).

---

> [← Seat Grant](05-seat-grant.md) · [Obsah](README.md) · [Floating protokol →](07-floating-protokol.md)
