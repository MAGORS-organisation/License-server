# 11. Prevádzka

> Časť zadania **Symbolon — licenčný server na .NET 10**. Späť na [obsah](README.md) · [prehľad projektu](../README.md).

## 11.1 Observabilita

- **OpenTelemetry** (`OpenTelemetry.Instrumentation.AspNetCore` 1.18.0) + vstavané .NET 10 metre (`Microsoft.AspNetCore.Hosting`, `Kestrel`, `RateLimiting`, `Authorization`).
- **Doménové metriky** — toto je to, čo správca licencií u zákazníka naozaj chce:

| Metrika | Typ | Popis |
|---|---|---|
| `symbolon.seats.active` | UpDownCounter | Aktuálne držané sedadlá (label: license, relay) |
| `symbolon.seats.peak` | Gauge | Špičková súbežnosť v okne |
| `symbolon.checkout.denied` | Counter | **Najdôležitejšia metrika pre upsell** — koľkokrát zákazník narazil na strop |
| `symbolon.checkout.duration` | Histogram | |
| `symbolon.lease.expired_without_release` | Counter | Indikátor padajúcich klientov |
| `symbolon.grant.remaining_ttl` | Gauge | Alert, keď sa relay blíži k expirácii grantu |
| `symbolon.verification.degraded` | Counter | Overenia bez PQ podpisu (viď 9.4/5) |
| `symbolon.clock.skew_detected` | Counter | |

Priložený **Grafana dashboard** ako JSON v `deploy/` — pri infraštruktúrnom OSS produkte je to prekvapivo silný adopčný faktor.

- **Health checks**: `/health/live` (proces žije) a `/health/ready` (DB, key ring, platnosť grantov). Relay pridáva `/health/grant` s dňami do expirácie.

## 11.2 SLO (pre vendor-hosted variant)

| SLO | Cieľ |
|---|---|
| Dostupnosť `/v1/leases` | 99,9 % mesačne |
| p99 checkout | < 250 ms |
| Trvanie výpadku bez dopadu na bežiacich klientov | ≥ `graceTtl` (default 4 h) |
| RPO / RTO | 5 min / 30 min |

## 11.3 Nasadenie a upgrade

- Helm chart + `docker compose` + systemd unit pre relay (jeden binár, žiadny Docker u zákazníka — dôležité pre priemyselné prostredia).
- **Aspire 13.5** ako vývojárska orchestrácia a generátor Helm/Compose artefaktov, **nie** ako runtime závislosť používateľa. Toto je vedomé — nechceme nútiť zákazníkov do Aspire.
- Migrácie: `dotnet ef migrations bundle` → samostatný spustiteľný súbor; nikdy `EnsureCreated`. Pravidlo expand-contract, žiadna migrácia neblokuje beh starej verzie.
- Kompatibilita: control plane v N podporuje relaye N−1 a N−2; klientske SDK je spätne kompatibilné 3 major verzie (kritické — SDK je zapečené v produktoch, ktoré zákazník aktualizuje raz za roky).

---

[← 10. Testovacia stratégia](10-testovacia-strategia.md) · [Obsah](README.md) · [12. OSS governance a monetizácia →](12-oss-governance.md)
