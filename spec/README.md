# spec/ — otvorená špecifikácia

**Licencia: [CC BY 4.0](LICENSE)** (`CC-BY-4.0`) — odlišná od zvyšku repozitára.
Viď [LICENSING.md](../LICENSING.md).

Tento adresár je zatiaľ **prázdny až na licenciu**. Patrí sem to, čo musí byť
implementovateľné kýmkoľvek — vrátane konkurencie — inak to nie je otvorený formát:

| Obsah | Zdroj v zadaní |
|---|---|
| `symlic/1` — formát licenčného súboru (JWS General JSON, claims, pravidlá validácie) | kapitola [5.2](../docs/05-format-licencie.md) |
| License Key, Lease Token, Seat Grant (`.symgrant`), Revocation List (`.symrl`) | kapitola [5.1, 5.3–5.5](../docs/05-format-licencie.md) |
| Floating protokol — checkout / heartbeat / renew / release, časovanie | kapitola [7](../docs/07-floating-protokol.md) |
| Testovacie vektory (vrátane FIPS 204 KAT) | kapitola [10.4](../docs/10-testovacia-strategia.md) |

Kapitoly v `docs/` sú **zadanie**, nie normatívna špecifikácia. Prepis do `spec/`
je samostatný krok — až vtedy sa na tento obsah vzťahuje CC BY 4.0.
