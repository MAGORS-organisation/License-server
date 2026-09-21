# Návod na Migráciu z FlexNet Publisher (FLEXlm) na Symbolon

Tento dokument je praktickou príručkou pre architektov, DevOps inžinierov a správcov licencií, ktorí migrujú podnikovú infraštruktúru z proprietárneho systému **FlexNet Publisher (FLEXlm)** na moderný open-source systém **Symbolon**.

---

## 1. Motivácia a Prehľad Rozdielov

FlexNet Publisher vznikol koncom 80. rokov 20. storočia a dodnes si nesie dedičstvo monolitickej architektúry:
- **Proprietárne vendor daemony (`mysw_vd`)**: Vyžadujú špecifické binárne kompilácie pre každý OS, otváranie neštandardných portov v firewalle a trpia pádmi pri sieťových anomáliách.
- **Krehké Triad klastre (Quorum 2 z 3)**: Ak vypadne jeden server alebo nastane split-brain medzi dátovými centrami, celý licenčný fond sa okamžite zablokuje a aplikácie po celom svete prestanú fungovať.
- **Kryptografické obmedzenia**: Zastarané symetrické šifry a proprietary seed kľúče, ktoré nespĺňajú moderné bezpečnostné požiadavky (ako je európsky **Cyber Resilience Act** alebo prechod na **Post-Kvantovú Kryptografiu**).
- **Zložitá diagnostika**: Klientske aplikácie hlásia nejasné numerické kódy (napr. `FLEXlm error -15,10: Cannot connect to license server`, `FLEXlm error -4: Licensed number of users already reached`).

### Prečo Symbolon?
1. **Deterministická alokácia v $O(1)$**: PostgreSQL 17 / SQLite s `FOR UPDATE SKIP LOCKED` garantuje 0 prečerpaných sedadiel bez distribuovaných zámkov.
2. **Post-Kvantová Kryptografia (FIPS 204)**: Dvojitý hybridný podpis **ES256 + ML-DSA-65** chráni dlhoveké licencie pred budúcimi kvantovými počítačmi.
3. **Delegated Seat Grant namiesto Triadov**: Pobočkové servery (**Relay**) dostávajú časovo ohraničený blok sedadiel (`.symgrant`) a fungujú úplne autonómne aj pri výpadku pripojenia do cloudu.
4. **Otvorený protokol a formáty**: Žiadny binárny lock-in. Všetko je postavené na štandardoch IETF: JWS (RFC 7515), JWKS (RFC 9964), REST a JSON.

---

## 2. Prevodový Slovník Pojmov

| FlexNet Publisher (FLEXlm) | Symbolon Ekvivalent | Popis a Rozdiel v Symbolone |
|---|---|---|
| `lmgrd` (License Manager Daemon) | `Symbolon.ControlPlane` | Štandardná REST Minimal API služba v .NET 10 bežiaca v Docker kontajneri alebo K8s podoch. |
| Vendor Daemon (`mysw_vd`) | Integrované v ControlPlane | Odpadá nutnosť udržiavať binárne závislý vendor démon; logika sedadiel je natívnou súčasťou enginu. |
| Licenčný súbor (`license.dat`) | `.symlic` súbor (RFC 7468 PEM) | Čitateľná PEM obálka s JWS payloadom a hybridnými podpismi (ES256 + ML-DSA-65). |
| Options súbor (`mysw.opt`) | Policy JSON (`policy.json`) | Štruktúrovaná deklaratívna konfigurácia pravidiel, kvót, rezervácií a TTL limitov. |
| Triad Redundancy (3 servery) | PostgreSQL HA + Delegated Grants | Namiesto citlivého quóra používa ControlPlane štandardnú DB replikáciu a lokálne Relay uzly. |
| `lmutil lmstat` | `Symbolon.Cli` & `/v1/leases` | Okamžitý prehľad o obsadenosti cez CLI alebo prehľadný Retro Web TUI Dashboard v reálnom čase. |
| `lmutil lmreread` | Dynamický reload API | Úpravy politík a rotácie kľúčov prebiehajú bez reštartu a bez prerušenia bežiacich sedadiel. |
| License Borrowing | `POST /v1/leases/{id}/borrow` | Zapožičanie sedadla na $N$ dní pre offline stanice s kryptografickým lease tokenom. |
| HostID / Dongle / MAC | Hardvérový Fingerprint | Kanonický deterministický SHA-256 hash z CPU ID, MAC a UUID dosky s toleranciou výmeny komponentu. |

---

## 3. Prevod Options Súboru (`.opt`) na Symbolon Policy

V tradičnom FlexNete sa pravidlá prideľovania riadili súborom `options.opt`. V Symbolone sa tieto pravidlá zapisujú do prehľadného JSON dokumentu politiky (pozrite vzory v [`deploy/config/`](../deploy/config/)):

### Porovnanie syntaxe:

#### 1. Rezervácia sedadiel pre tím
- **FlexNet (`mysw.opt`)**:
  ```text
  GROUP engineering jan.novak peter.kovac maria.horvathova
  RESERVE 5 CAD_PRO GROUP engineering
  ```
- **Symbolon (`symbolon-policy-sample.json`)**:
  ```json
  {
    "groups": [{ "name": "engineering", "members": ["jan.novak", "peter.kovac", "maria.horvathova"] }],
    "seatAllocations": [{
      "featureCode": "CAD_PRO",
      "groupName": "engineering",
      "type": "Reservation",
      "guaranteedSeats": 5
    }]
  }
  ```

#### 2. Obmedzenie počtu sedadiel pre externistov
- **FlexNet (`mysw.opt`)**:
  ```text
  GROUP contractors externista1 externista2
  MAX 2 CAD_PRO GROUP contractors
  ```
- **Symbolon (`symbolon-policy-sample.json`)**:
  ```json
  {
    "groups": [{ "name": "contractors", "members": ["externista1", "externista2"] }],
    "seatAllocations": [{
      "featureCode": "CAD_PRO",
      "groupName": "contractors",
      "type": "Limit",
      "maxSeats": 2
    }]
  }
  ```

#### 3. Riadenie offline zapožičiavania (Borrowing)
- **FlexNet (`mysw.opt`)**:
  ```text
  BORROW_LOWWATER CAD_PRO 3
  MAX_BORROW_HOURS CAD_PRO 168
  ```
- **Symbolon (`symbolon-policy-sample.json`)**:
  ```json
  {
    "borrowing": {
      "enabled": true,
      "maxBorrowDurationDays": 7,
      "minAvailableSeatsForBorrow": 3,
      "allowEarlyReturn": true
    }
  }
  ```

---

## 4. Architektonické Porovnanie: Triad vs. Delegated Seat Grant

```
FLEXNET TRIAD (Zastarané & Krehké):
┌──────────────┐      Heartbeat Ping     ┌──────────────┐
│ Master srv01 │ <─────────────────────> │ Backup srv02 │
└──────┬───────┘   (Strata spojenia =    └──────┬───────┘
       │            split-brain kolaps)         │
       └───────────────────┬────────────────────┘
                           ▼
                 ┌──────────────────┐
                 │ Quorum srv03     │
                 └──────────────────┘
       (Ak vypadnú 2 servery z 3, licencie sú nedostupné)

SYMBOLON ARCHITEKTÚRA (Moderná, Odolná & Škálovateľná):
┌────────────────────────────────────────────────────────┐
│  Symbolon ControlPlane (Cloud / Datacentrum)           │
│  - PostgreSQL 17 HA (Patroni / Managed Cloud DB)       │
│  - Horizontálne škálovanie bez stavu (Stateless pods)  │
└──────────────────────────┬─────────────────────────────┘
                           │ Vydanie .symgrant (časovo ohraničený)
                           ▼
┌────────────────────────────────────────────────────────┐
│  On-Premise Symbolon Relay (Pobočka / Závod)          │
│  - Beží na lokálnom hardvéri alebo v Docker kontajneri │
│  - Perzistentný SQLite WAL fond pre lokálne stanice   │
│  - Funguje 100% autonómne aj pri úplnom výpadku WAN!   │
└────────────────────────────────────────────────────────┘
```

### Prečo je model Symbolon lepší:
1. **Žiadny split-brain kolaps**: Lokálne stanice na pobočke komunikujú priamo s lokálnym Relayom. Výpadok internetového spojenia do centrály nemá žiadny vplyv na prácu inžinierov na pobočke počas trvania grantu (napr. 7 dní).
2. **Deterministická spotreba**: Relay má presne pridelenú kvótu sedadiel, takže nemôže dôjsť k prečerpaniu licenčného fondu ISV dodávateľa.

---

## 5. Štvorfázová Stratégia Bezvýpadkovej Migrácie (Dual-Run)

Aby prechod nespôsobil prestoje v produkcii, odporúčame nasledujúci osvedčený postup:

```
Fáza 1: Príprava a Paralelné Nasadenie
  ├─ Nasadenie Symbolon ControlPlane v Docker/K8s
  └─ Vygenerovanie podpisových kľúčov a prevod .opt pravidiel do policy.json

Fáza 2: Duálny Beh v Klientoch (Dual-Run)
  ├─ ISV aplikácia najprv požiada Symbolon SDK
  └─ V prípade dočasnej nedostupnosti fallback na starý FLEXlm lmgrd

Fáza 3: Nasadenie Lokálnych Relay Uzlov
  ├─ Inštalácia Symbolon Relay na vzdialené pobočky a do tovární
  └─ Prepnutie offline workerov na Symbolon Borrow

Fáza 4: Úplné Odstavenie FlexNetu
  ├─ Vypnutie lmgrd a vendor daemona
  └─ Odstránenie starého FLEXlm kódu z aplikácií
```

### Ukážka Dual-Run Wrapperu v C#:
```csharp
public async Task<ISeatLeaseHandle> AcquireLicenseWithFallbackAsync(string feature)
{
    try
    {
        // 1. Primárny pokus: Moderný Symbolon server
        var lease = await _symbolonClient.AcquireSeatAsync([feature]);
        if (lease.Acquired)
        {
            return new SymbolonLeaseWrapper(lease);
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Symbolon server nedostupný, prepínam na FlexNet fallback...");
    }

    // 2. Záložný pokus: Pôvodný FlexNet lmgrd
    return AcquireLegacyFlexNetSeat(feature);
}
```

---

## 6. Časté Otázky a Riešenie Problémov (FAQ)

**Otázka: Čo sa stane s našimi existujúcimi MAC adresami a hostid?**  
*Odpoveď*: Symbolon podporuje kanonický zber hardvérových komponentov vrátane MAC adries ethernetových adaptérov. Pri migrácii node-locked licencií môžete použiť rovnaké MAC adresy ako vstupné komponenty pre Symbolon viazanie.

**Otázka: Podporuje Symbolon reportovanie spotreby pre interné rozúčtovanie (chargeback)?**  
*Odpoveď*: Áno. Symbolon obsahuje kryptograficky chránený `audit_ledger`, ktorý zaznamenáva každú udalosť (kto, kedy, na akom stroji a koľko minút mal licenciu alokovanú) a vstavané Prometheus metriky `symbolon_seats_active` s dimenziami pre produkt a tenanta.

**Otázka: Kde nájdem vzorové konfiguračné súbory?**  
*Odpoveď*: V repozitári v priečinku [`deploy/config/`](../deploy/config/):
- [`flexnet-options-sample.opt`](../deploy/config/flexnet-options-sample.opt) — pôvodný FlexNet vzor.
- [`symbolon-policy-sample.json`](../deploy/config/symbolon-policy-sample.json) — ekvivalentná Symbolon konfigurácia.
- [`flexnet-license-sample.lic`](../deploy/config/flexnet-license-sample.lic) — pôvodný licenčný súbor.
- [`symbolon-license-sample.symlic`](../deploy/config/symbolon-license-sample.symlic) — moderná `symlic/1` licencia v PEM formáte.
