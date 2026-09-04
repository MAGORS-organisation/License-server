# 10. Testovacia stratégia

> Časť zadania **Symbolon — licenčný server na .NET 10**. Späť na [obsah](README.md) · [prehľad projektu](../README.md).

## 10.1 Princíp

Tento produkt má **dva** kritické invarianty a všetko ostatné je bežný CRUD:

> **I1.** Počet súbežne držaných sedadiel pre licenciu nikdy neprekročí `maxSeats + overage`.
> **I2.** Dokument, ktorý neprešiel požadovanými podpismi, sa nikdy nepovažuje za platný.

Testovací rozpočet ide primárne sem. Zvyšok pokrývame štandardne.

## 10.2 Nástroje

| Vrstva | Nástroj | Verzia (09/2026) |
|---|---|---|
| Test framework | **xUnit v3** + Microsoft.Testing.Platform | 4.0.0 / MTP 2.3.3 |
| Property-based + **konkurenčné** testy | **CsCheck** (má model-based a parallel testing so shrinkingom) | 4.8.0 |
| Snapshot | **Verify.XunitV3** (nie `Verify.Xunit` — deprecated) | aktuálna |
| Integračné | **Testcontainers.PostgreSql** | 4.14.0 |
| Aplikačné/E2E | `Microsoft.AspNetCore.Mvc.Testing` / `WebApplicationFactory` | 10.0.11 |
| Deterministický čas | `Microsoft.Extensions.TimeProvider.Testing` → `Microsoft.Extensions.Time.Testing.FakeTimeProvider` | 10.9.0 |
| Mutačné testy | **Stryker.NET** — *overiť podporu xUnit v3/MTP pred nasadením* | 4.16.0 |
| Záťaž | **k6** (primárne) / NBomber — *overiť licenciu NBomber pred adopciou* | — |
| Statická analýza | Roslyn analyzers `latest-all`, `TreatWarningsAsErrors`, Semgrep, CodeQL | — |

## 10.3 Testovacia pyramída a ciele pokrytia

| Vrstva | Počet (odhad V1) | Cieľ pokrytia | Cieľ mutation score |
|---|---|---|---|
| Unit — `Symbolon.Crypto` | ~120 | 95 % | **≥ 85 %** |
| Unit — `Symbolon.Format` | ~150 | 95 % | **≥ 85 %** |
| Unit — `Symbolon.Domain` (lease/policy) | ~250 | 90 % | **≥ 80 %** |
| Property-based | ~25 vlastností | — | — |
| Integračné (DB) | ~90 | 80 % | — |
| Aplikačné (E2E) | ~45 scenárov | — | — |
| Záťažové | 6 profilov | — | — |

Mutation score je tu dôležitejší než line coverage: pri kryptografickom kóde je triviálne mať 100 % pokrytie a nulovú detekčnú schopnosť.

## 10.4 Unit testy — kryptografia a formát

**Povinné prípady (výber, každý je regresný test na reálnu triedu chýb):**

```csharp
public class LicenseDocumentVerifierTests
{
    [Fact] public void Valid_hybrid_document_verifies() { }

    // I2 — najdôležitejší test v projekte
    [Fact] public void Corrupted_pq_signature_is_fatal_not_skipped()
    {
        var doc = SignedFixture.Hybrid();
        var tampered = doc.WithSignatureBitFlipped(alg: Alg.MlDsa65);
        var r = Verifier(supports: [Alg.Es256, Alg.MlDsa65]).Verify(tampered);
        Assert.False(r.IsValid);
        Assert.StartsWith("bad-signature", r.FailureReason);   // NIE "no-verifiable-signature"
    }

    [Fact] public void Stripped_pq_signature_rejected_when_verifier_supports_it()
    {
        var stripped = SignedFixture.Hybrid().WithoutSignature(Alg.MlDsa65);
        var r = Verifier(supports: [Alg.Es256, Alg.MlDsa65]).Verify(stripped);
        Assert.False(r.IsValid);
        Assert.StartsWith("stripped-signature", r.FailureReason);
    }

    [Theory]
    [InlineData(Strictness.Strict,  false)]
    [InlineData(Strictness.Auto,    false)]   // exp - iat = 5 rokov ⇒ dlhoveké ⇒ odmietnuť
    [InlineData(Strictness.Lenient, true)]
    public void Missing_pq_on_unsupported_platform_follows_policy(Strictness s, bool expected)
        => Assert.Equal(expected,
             Verifier(supports: [Alg.Es256], strictness: s)
                .Verify(SignedFixture.Hybrid(lifetime: TimeSpan.FromDays(365 * 5))).IsValid);

    [Fact] public void Payload_modified_after_signing_is_rejected() { }
    [Fact] public void Unknown_crit_header_is_rejected() { }
    [Fact] public void Revoked_kid_is_rejected_even_with_valid_signature() { }
    [Fact] public void Document_with_iat_older_than_high_water_mark_is_rejected() { }
    [Fact] public void Signature_of_wrong_length_is_rejected_without_calling_provider() { }
    [Fact] public void Alg_confusion_es256_signature_presented_as_mldsa_is_rejected() { }
    [Fact] public void Empty_signatures_array_is_rejected() { }
    [Fact] public void Duplicate_alg_entries_do_not_double_count_as_verified() { }
    [Fact] public void Pem_with_trailing_garbage_is_rejected() { }
}
```

**Známe odpovede (KAT) pre FIPS 204.** `Symbolon.Crypto.Tests` obsahuje NIST ACVP vektory pre ML-DSA-44/65/87 (sign/verify) a beží ich pri každom builde. Dôvod: keď v budúcnosti vymeníme implementáciu (natívne ↔ BouncyCastle fallback), musíme mať dôkaz bit-za-bitovej zhody. Testy sa preskočia s explicitným `Skip` a hlásením, ak `!MLDsa.IsSupported` — a CI matrix **musí** obsahovať aspoň jeden runner, kde podporované sú (Linux + OpenSSL 3.5+), inak by sa preskakovali potichu navždy.

**Cross-implementačný test:** ten istý vektor podpísaný natívne sa musí overiť managed fallbackom a naopak.

## 10.5 Property-based a konkurenčné testy — invariant I1

Toto je najhodnotnejšia časť testovacej sady, lebo klasické unit testy race conditions nechytia.

```csharp
public class SeatPoolProperties
{
    // Vlastnosť 1: nikdy viac držaných sedadiel než limit — pri ĽUBOVOĽNOM prekladaní operácií
    [Fact]
    public void Concurrent_operations_never_exceed_seat_limit()
    {
        Gen.Int[1, 20].SelectMany(limit =>
            Gen.OneOf(
                Gen.Const<Op>(new Checkout()),
                Gen.Const<Op>(new Release()),
                Gen.Const<Op>(new Renew()),
                Gen.Const<Op>(new AdvanceClock(TimeSpan.FromMinutes(3))))
               .Array[1, 200].Select(ops => (limit, ops)))
        .Sample((limit, ops) =>
        {
            var pool = new InMemorySeatPool(limit, new FakeTimeProvider());
            Parallel.ForEach(ops, op => op.Apply(pool));
            return pool.ActiveSeats <= limit;      // I1
        }, iter: 100_000);
    }

    // Vlastnosť 2: lineárnosť — súbežné vykonanie musí zodpovedať NEJAKÉMU sériovému poradiu
    // CsCheck sa používa fluent: gen.SampleModelBased(...) / gen.SampleParallel(...)
    [Fact]
    public void Checkout_release_is_linearizable()
        => GenSeatPool.SampleParallel(
               operations: GenOp.Array[1, 12],
               initial: () => new SeatPool(limit: 3),
               equal: (a, b) => a.Snapshot() == b.Snapshot());   // shrinking je automatický

    // Vlastnosť 3: žiadne sedadlo nie je držané dvomi lease naraz
    [Fact] public void No_seat_held_twice() { }

    // Vlastnosť 4: uvoľnené sedadlo je vždy znovu získateľné
    [Fact] public void Released_seat_becomes_available() { }

    // Vlastnosť 5: resurrection vráti sedadlo PÔVODNÉMU držiteľovi, nikdy inému
    [Fact] public void Resurrection_never_reassigns_to_a_different_fingerprint() { }

    // Vlastnosť 6: súčet sedadiel vo všetkých platných grantoch <= maxSeats
    [Fact] public void Grants_never_oversubscribe_license() { }
}
```

CsCheck je zvolený zámerne: má **model-based** a **paralelné** testovanie so **zmenšovaním protipríkladov**, čo je presne to, čo pri lease engine potrebuješ. FsCheck to nemá v tejto podobe.

## 10.6 Integračné testy (Testcontainers + Postgres)

Sem patrí všetko, čo závisí od skutočnej sémantiky databázy — a to je pri `SKIP LOCKED` a `EXCLUDE USING gist` podstatná časť korektnosti.

| Test | Overuje |
|---|---|
| `Fifty_parallel_checkouts_on_ten_seats_yield_exactly_ten_successes` | `SKIP LOCKED` skutočne serializuje alokáciu |
| `Overlapping_seat_grants_are_rejected_by_the_database` | `EXCLUDE USING gist` funguje (nezávisí od kódu) |
| `Expired_lease_is_reacquired_after_ttl_plus_resurrection_window` | Časová logika v SQL |
| `Renew_with_stale_sequence_is_rejected` | T5 |
| `Renew_from_different_fingerprint_is_rejected` | T4 |
| `Audit_chain_hash_is_unbroken_after_1000_events` | Integrita ledgeru |
| `Audit_table_rejects_UPDATE_and_DELETE` | Práva v DB, nie v kóde |
| `Idempotent_checkout_returns_same_lease_within_window` | Nezožerie dve sedadlá pri retry |
| `Migration_up_down_up_is_clean_on_populated_database` | Migrácie |
| `Same_domain_tests_pass_against_SqliteSeatStore` | Relay a control plane sa nerozídu |

## 10.7 Aplikačné (E2E) testy

Postavené na `WebApplicationFactory` s reálnym Postgres kontajnerom a `FakeTimeProvider` vpichnutým cez DI. Žiadne `Thread.Sleep` — čas sa posúva explicitne.

**Scenáre (výber z 45):**

| # | Scenár | Kroky | Očakávanie |
|---|---|---|---|
| S1 | Šťastná cesta floating | issue → checkout → 3× heartbeat → release | Sedadlo voľné, audit má 5 udalostí, reťaz hashov sedí |
| S2 | Vyčerpaný pool | 25 checkoutov na 25 sedadiel, 26. checkout | `409` + `problem+json` typ `seat-pool-exhausted` + `Retry-After` |
| S3 | Pád klienta | checkout, žiadny heartbeat, `Advance(ttl + resurrection)` | Sedadlo automaticky voľné, audit `expire` |
| S4 | Spánok notebooku | checkout, `Advance(ttl + 1 min)`, renew | **Resurrection**: to isté sedadlo, ten istý `seat_no` |
| S5 | Spánok pridlho | checkout, `Advance(ttl + resurrection + 1 min)`, renew | `410 lease-unknown`, klient robí nový checkout |
| S6 | Výpadok relayu | checkout, zabiť relay, klient beží | Klient pokračuje `graceTtl`, potom degraduje; **nové** checkouty zamietnuté |
| S7 | Obnova po výpadku | S6 + naštartovať relay | Zosúladenie bez duplicity; audit obsahuje obe strany |
| S8 | HA cez disjunktné granty | 2 relaye × 12 sedadiel, zabiť jeden | 12 sedadiel ďalej funguje; celkovo nikdy > 24 |
| S9 | Air-gapped | `grant:request` → offline `grant:issue` → `grant:import` → checkout | Funguje bez siete; ďalší grant vyžaduje `usageDigest` |
| S10 | Borrow a predčasné vrátenie | borrow 5 dní → offline práca → vrátenie s PoP | Sedadlo voľné pred `borrowedUntil` |
| S11 | Borrow bez PoP | pokus o vrátenie cudzieho borrow | `403` |
| S12 | Rollback hodín u klienta | posunúť klientský čas o −2 dni | Klient odmietne stav, vyžiada nový checkout; server nedotknutý |
| S13 | Revokácia | revoke → klient stiahne `.symrl` | Do `revocation.maxAge` klient prestane fungovať |
| S14 | Revokácia `kid` | revoke kľúč | Všetky dokumenty s tým `kid` neplatné, aj nevypršané |
| S15 | Rotácia kľúča s prekryvom | vydať nový Product kľúč, staré licencie | Staré aj nové platné počas prekryvu |
| S16 | Migrácia PQC | klient bez PQC + dlhoveká licencia | `Strictness.Auto` odmietne, telemetria hlási `degraded-verification` |
| S17 | Kontajnerový fingerprint | 5 replík z rovnakého image | 5 rôznych fingerprintov (random UUID), nie 1 |
| S18 | Rezervácia | 5 rezervovaných pre skupinu, ostatní vyčerpajú zvyšok | Člen skupiny sa dostane k sedadlu |
| S19 | Fronta | pool plný, `allowQueue`, uvoľnenie | `202` → sedadlo pridelené vo FIFO poradí |
| S20 | Idempotencia pri timeoute | checkout, timeout, retry s rovnakým `Idempotency-Key` | 1 sedadlo, nie 2 |

## 10.8 Chaos a odolnosť

- **DB failover počas checkoutu** (Testcontainers + `pg_terminate_backend`): žiadne stratené ani duplicitné sedadlo; retry politika Npgsql nesmie zopakovať už vykonaný `UPDATE` bez idempotencie.
- **Posun hodín na serveri** (`FakeTimeProvider` + reálny NTP skok v kontajneri): leases nesmú hromadne expirovať pri skoku dopredu o 1 h.
- **Zaplnenie disku relayu** (SQLite): server musí prejsť do read-only režimu s jasnou chybou, nie tichou stratou auditu.
- **Sieťová partícia relay↔control plane** počas obnovy grantu: relay dobehne na starom grante, nesmie vydávať nad limit.

## 10.9 Záťaž — cieľové čísla

Profily merané cez k6 proti control plane (4 vCPU) a relayu (2 vCPU):

| Profil | Cieľ | Prah zlyhania |
|---|---|---|
| Checkout burst (500 klientov na 1 licenciu naraz) | p99 < 250 ms, 0 pretečení | akékoľvek pretečenie = fail |
| Heartbeat steady state (5 000 klientov, interval 2 min ⇒ ~42 rps) | p99 < 50 ms, CPU < 30 % | p99 > 150 ms |
| Vydanie licenčného súboru (hybridný podpis) | ≥ 200 dokumentov/s na jadro | < 100/s |
| Overenie na klientovi | ES256 < 1 ms, ML-DSA-65 < 3 ms | > 10 ms (blokovalo by štart aplikácie) |
| Relay (AOT) štart | < 200 ms, RSS < 60 MB | > 1 s |

Podpisovanie ML-DSA je pomalšie a ~3,3 kB podpis je väčší — preto sa **licenčné súbory cachujú** a nevydávajú pri každom volaní.

## 10.10 CI/CD

```yaml
# .github/workflows/ci.yml (skrátené)
jobs:
  build:
    strategy:
      matrix:
        os: [ubuntu-24.04, windows-2025, macos-15]
    steps:
      - dotnet test --configuration Release          # xUnit v3 cez MTP
      # ubuntu-24.04 musí mať OpenSSL >= 3.5 => tam bežia PQC KAT testy
      - name: Assert PQC coverage
        if: matrix.os == 'ubuntu-24.04'
        run: ./scripts/assert-pqc-tests-ran.sh        # zlyhá, ak sa PQC testy PRESKOČILI
  quality:
    - dotnet stryker --threshold-break 80 --project Symbolon.Crypto
    - dotnet stryker --threshold-break 80 --project Symbolon.Domain
  contract:
    - dotnet build -p:OpenApiGenerateDocumentsOnBuild=true
    - git diff --exit-code spec/openapi.json          # zmena kontraktu musí byť vedomá
  supplychain:
    - dotnet restore --locked-mode
    - cyclonedx dotnet ... -o sbom.json
    - cosign sign-blob ...
```

Krok `assert-pqc-tests-ran.sh` je dôležitejší, než vyzerá: bez neho sa PQC testy jedného dňa začnú ticho preskakovať (zmena base image, downgrade OpenSSL) a nikto si to nevšimne, kým nevydá nepodpísanú licenciu.

---

[← 9. Bezpečnosť](09-bezpecnost.md) · [Obsah](README.md) · [11. Prevádzka →](11-prevadzka.md)
