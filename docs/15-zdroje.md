# 15. Zdroje

> Časť zadania **Symbolon — licenčný server na .NET 10**. Späť na [obsah](README.md) · [prehľad projektu](../README.md).

**Štandardy a regulácia**
- [RFC 9964 — ML-DSA for JOSE and COSE](https://datatracker.ietf.org/doc/draft-ietf-cose-dilithium/) (Proposed Standard, máj 2026; JOSE `alg` `ML-DSA-44/65/87`, COSE `-48/-49/-50`, `kty: AKP`)
- [RFC 7515 — JSON Web Signature](https://www.rfc-editor.org/rfc/rfc7515) · [RFC 9457 — Problem Details](https://www.rfc-editor.org/rfc/rfc9457)
- [FIPS 204 — ML-DSA](https://csrc.nist.gov/pubs/fips/204/final) · [FIPS 205 — SLH-DSA](https://csrc.nist.gov/pubs/fips/205/final)
- [EÚ: posilnenie kyberbezpečnosti postkvantovou kryptografiou](https://digital-strategy.ec.europa.eu/en/news/eu-reinforces-its-cybersecurity-post-quantum-cryptography) (NIS Cooperation Group roadmapa, 23. 6. 2025)
- [Cyber Resilience Act](https://digital-strategy.ec.europa.eu/en/policies/cyber-resilience-act)

**.NET 10**
- [What's new in .NET 10](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview) · [What's new in ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0)
- [`MLDsa` API](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.mldsa?view=net-10.0) · [`CompositeMLDsaAlgorithm`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.compositemldsaalgorithm?view=net-10.0)
- [Post-Quantum Cryptography in .NET 10 (Duende)](https://duendesoftware.com/blog/20260514-post-quantum-cryptography-in-dotnet-10)
- [PQC pre .NET 10 — dotnet/runtime#113498](https://github.com/dotnet/runtime/issues/113498) · [Ed25519 API proposal (milestone .NET 11) — dotnet/runtime#63174](https://github.com/dotnet/runtime/issues/63174)
- [EF Core 10 — čo je nové](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew) · [Npgsql 10 release notes](https://www.npgsql.org/efcore/release-notes/10.0.html) · [Npgsql concurrency tokens (`xmin`)](https://www.npgsql.org/efcore/modeling/concurrency.html)
- [ASP.NET Core Native AOT](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot) · [EF Core NativeAOT — „highly experimental"](https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries)
- [Certificate authentication (mTLS)](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/certauth) · [Passkeys v ASP.NET Core Identity 10](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/)
- [Aspire 13.5](https://aspire.dev/whats-new/aspire-13-5/)

**Konkurencia a doména**
- [Keygen je Fair Source (FCL-1.0-ALv2)](https://keygen.sh/blog/keygen-is-now-fair-source/) · [Keygen policies API](https://keygen.sh/docs/api/policies/) · [Keygen cryptography](https://keygen.sh/docs/api/cryptography/) · [Keygen floating licenses](https://keygen.sh/docs/choosing-a-licensing-model/floating-licenses/) · [Ako generovať licenčné kľúče (limity vynucovania)](https://keygen.sh/blog/how-to-generate-license-keys/)
- [keygen-relay (MIT)](https://github.com/keygen-sh/keygen-relay) · [air-gapped activation example](https://github.com/keygen-sh/air-gapped-activation-example)
- [Keygate (AGPL)](https://github.com/tabloy/keygate) · [Standard.Licensing](https://github.com/junian/Standard.Licensing)
- [FlexNet — Three-Server Redundancy](https://docs.revenera.com/fnp/2023r1/LicAdmin_Guide/Content/helplibrary/Using_Other_Capabilities_with_Three_Server_Redundancy.htm) · [FlexNet borrow](https://docs.revenera.com/fnp/2022r3/LicAdmin_Guide/Content/helplibrary/Initiating_Borrowing.htm) · [OpenLM: porovnanie redundancie (Triad/HAL/DSLS)](https://www.openlm.com/knowledge-base/license-server-redundancy-constellations-flexera-triad-ibm-high-availability-licensing-hal-dsls-cluster/)
- [LM-X GRACE](https://docs.x-formation.com/display/LMX/GRACE) · [LM-X kontrola hodín](https://docs.x-formation.com/display/LMX/System+clock+check) · [Sentinel: ochrana proti manipulácii s časom](https://docs.sentinel.thalesgroup.com/softwareandservices/rms/RMSDocumentation/APIREF/Content/APICustomizations/Protection%20Against%20Time%20Tampering.htm) · [Sentinel commuter](https://docs.sentinel.thalesgroup.com/softwareandservices/rms/RMSDocumentation/SysAdmin/Content/Using_Wcommute.htm)
- [Cryptlex node-locked / fuzzy matching](https://cryptlex.com/docs/licensing-models/node-locked-licenses) · [NetLicensing: fingerprint v Docker/VM](https://netlicensing.io/wiki/faq-docker-vm-environment) · [LicenseSpring floating server](https://docs.licensespring.com/license-entitlements/floating-licenses/floating-license-server)

**Poznámka k spoľahlivosti zdrojov.** Údaje o cenách komerčných produktov a niektoré defaultné hodnoty (LicenseSpring floating timeout, FLEXlm `MAX_OVERDRAFT`) sa nepodarilo overiť z primárnych zdrojov a v dokumente sú buď vynechané, alebo označené. Pred použitím v obchodnej argumentácii ich over priamo u dodávateľa. Dátumy CRA sú zo sekundárnych zdrojov — over v oficiálnom texte nariadenia.

---

*Dokument pripravený ako podklad pre rozhodnutie. Nie je právnym ani bezpečnostným posudkom.*

---

[← 14. Otvorené otázky — rozhodnutia, ktoré musíš urobiť ty](14-otvorene-otazky.md) · [Obsah](README.md)
