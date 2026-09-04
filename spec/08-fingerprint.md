# 8. Fingerprint a node-locking

> [← Floating protokol](07-floating-protokol.md) · [Obsah](README.md)

Fingerprint je identifikátor stroja, na ktorý sa viaže node-locked licencia alebo
lease. Táto kapitola definuje jeho komponenty, spôsob porovnávania a — najdôležitejšie
— kedy sa **nesmie** použiť hardvér.

---

## 8.1 Komponenty

**FPR-1.** Fingerprint sa skladá z pomenovaných komponentov. Každý komponent je
VOLITEĽNÝ; SDK zbiera tie, ktoré sú na danej platforme dostupné.

| Kód | Windows | Linux | macOS |
|---|---|---|---|
| `machineId` | `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid` | `/etc/machine-id` | `IOPlatformUUID` |
| `board` | Win32_BaseBoard SerialNumber | DMI `board_serial` | — |
| `cpu` | CPUID vendor+model | `/proc/cpuinfo` | `sysctl machdep.cpu` |
| `disk` | serial systémového zväzku | `/dev/disk/by-id` | IOKit |
| `mac` | prvá non-virtuálna MAC | `/sys/class/net/*/address` | `ifconfig` |
| `host` | hostname (**pseudonymizovaný**) | | |

**FPR-2.** Komponent `host` NESMIE byť prenesený ani uložený v otvorenej podobe.
MUSÍ byť pseudonymizovaný hashovaním so soľou špecifickou pre licenciu.

**FPR-3.** Nedostupný komponent sa MUSÍ vynechať. Implementácia NESMIE dosadiť
zástupnú hodnotu (prázdny reťazec, `unknown`, nuly).

> **Zdôvodnenie (nenormatívne).** Zástupná hodnota je horšia než chýbajúci komponent:
> pri `match-most` by sa dva rôzne stroje s rovnakým zástupným `board` javili ako
> zhodnejšie, než v skutočnosti sú.

**FPR-4.** Súhrnný hash fingerprintu MUSÍ mať tvar `{algo}:{hex}`, kde `algo` je
`sha256`. Do hashu vstupujú komponenty zoradené podľa kódu, aby bol výsledok
deterministický.

---

## 8.2 Matching stratégie

**FPR-5.** Implementácia MUSÍ podporovať tieto stratégie:

| Stratégia | Zhoda nastáva, keď |
|---|---|
| `match-any` | súhlasí aspoň **jeden** komponent |
| `match-two` | súhlasia aspoň **dva** komponenty |
| `match-most` | súhlasí **väčšina** prítomných komponentov (**default**) |
| `match-all` | súhlasia **všetky** prítomné komponenty |

**FPR-6.** Default stratégia MUSÍ byť `match-most`.

**FPR-7.** Do počtu pri `match-most` a `match-all` sa započítavajú iba komponenty
prítomné **v oboch** porovnávaných fingerprintoch.

**FPR-8.** Ak je počet spoločných komponentov menší než 2, `match-most` sa MUSÍ
správať ako `match-all`.

> **Zdôvodnenie (nenormatívne).** Bez `FPR-8` by stroj s jediným spoločným komponentom
> prešiel cez `match-most` triviálne — „väčšina z jedného" je jeden.

**FPR-9.** Stratégia je súčasťou podpísaného licenčného súboru
(`symlic.binding.matching`). Klient ju NESMIE prepísať lokálnou konfiguráciou.

---

## 8.3 Kontajnery, VM a autoscaling

**Hardvérový fingerprint v kontajneri je anti-pattern.** Golden image nesie identický
`MachineGuid` / `machine-id` do všetkých inštancií; klon VM duplikuje všetko.

**FPR-10.** SDK MUSÍ detegovať kontajnerové a cloudové prostredie pred zberom
hardvérových komponentov.

**FPR-11.** Detekcia MUSÍ zahŕňať minimálne: existenciu `/.dockerenv`, obsah
`/proc/1/cgroup`, premennú prostredia `KUBERNETES_SERVICE_HOST` a dostupnosť
cloud metadata endpointov.

**FPR-12.** Ak je prostredie detegované ako kontajnerové alebo cloudové, SDK
**NESMIE** použiť hardvérový fingerprint. MUSÍ namiesto neho použiť:

1. perzistovaný náhodný UUID uložený vo zväzku (volume), alebo
2. cloud instance-id.

```
if (IsContainerized() || IsCloudInstance())
    → fingerprint = perzistovaný náhodný UUID vo volume
      (alebo cloud instance-id), NIE hardvér
    → a odporuč floating s krátkym TTL namiesto node-lock
```

**FPR-13.** SDK MAL BY v takom prípade zalogovať odporúčanie použiť **floating
s krátkym TTL** namiesto node-locku.

**FPR-14.** Toto správanie MUSÍ byť výslovne zdokumentované v používateľskej
dokumentácii.

> **Zdôvodnenie (nenormatívne).** Fingerprint v kontajneri je najčastejší zdroj
> supportných tiketov naprieč celou kategóriou produktov. `FPR-14` nie je formalita —
> je to jediné, čo tomu predchádza.

---

## 8.4 Aktivácia a deaktivácia

**FPR-15.** Node-lock aktivácia prebieha cez `POST /v1/activations`, deaktivácia cez
`DELETE /v1/activations/{id}`.

**FPR-16.** Vydavateľ MUSÍ vynútiť `machineUniqueness` z politiky — teda či sa ten istý
fingerprint smie objaviť pri viacerých licenciách.

**FPR-17.** Nezhoda fingerprintu MUSÍ viesť k odpovedi `403` s typom problému
`fingerprint-mismatch` podľa
[`FLT-29`](07-floating-protokol.md#761-chyby).

---

## 8.5 Ochrana osobných údajov

**FPR-18.** Komponenty fingerprintu sú údaje o zariadení zamestnanca zákazníka.
Vydavateľ NESMIE prenášať surové komponenty mimo prostredie zákazníka; prenáša sa
**iba súhrnný hash** podľa `FPR-4`.

**FPR-19.** Surové komponenty MÔŽU byť uložené lokálne relayom na účel `matching`.
Relay ich NESMIE zahrnúť do hlásenia využitia
([`FLT-36`](07-floating-protokol.md#78-hlásenie-využitia)).

> *Poznámka:* podrobnosti k GDPR — právny základ, retencia, rola spracovateľa verzus
> prevádzkovateľa — táto špecifikácia nedefinuje. Viď kapitolu 9.8 zadania.

---

> [← Floating protokol](07-floating-protokol.md) · [Obsah](README.md)
