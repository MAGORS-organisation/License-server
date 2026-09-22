# @symbolon/validator — WebAssembly & WebCrypto Offline License Validator SDK

Oficiálne klientske SDK pre nezávislých dodávateľov softvéru (ISV), ktorí potrebujú **100% offline validáciu licencií** vo webových prehliadačoch, Node.js, Electron, Tauri, React, Vue a Angular aplikáciách.

Využíva štandardizované rozhranie **W3C WebCrypto API** (`crypto.subtle`) pre kryptografické overovanie digitálnych podpisov **NIST P-256 (ES256)** a vstavaný generátor hardvérových odtlačkov pre **web-based node-locking**.

---

## Inštalácia

```bash
# Cez npm
npm install @symbolon/validator

# Alebo cez yarn / pnpm
pnpm add @symbolon/validator
```

Pre použitie priamo v prehliadači bez bundlerov:
```html
<script src="https://cdn.example.com/symbolon-validator.js"></script>
```

---

## Rýchly Štart (JavaScript / TypeScript)

### 1. Overenie licencie v prehliadači alebo Node.js

```javascript
import { SymbolonOfflineValidator } from '@symbolon/validator';

// 1. Získajte dôveryhodný JWKS (zo servera /v1/jwks alebo lokálneho JSON súboru)
const jwks = {
  keys: [
    {
      kty: "EC",
      crv: "P-256",
      x: "WKn-Ir...-8",
      y: "D_E...vQ",
      kid: "prod-key-2026",
      alg: "ES256"
    }
  ]
};

// 2. Vytvorte inštanciu validátora
const validator = new SymbolonOfflineValidator({
  jwks,
  expectedAudience: "my-cad-application" // Voliteľná kontrola produktu
});

// 3. Overte licenciu (.symlic PEM string alebo surový JWS token)
const rawLicense = `-----BEGIN SYMBOLON LICENSE-----
eyJhbGciOiJFUzI1NiIs...
-----END SYMBOLON LICENSE-----`;

const result = await validator.validate(rawLicense);

if (result.isValid) {
  console.log(`Licencia je PLATNÁ pre zákazníka: ${result.customer}`);
  console.log(`Kapacita: ${result.maxSeats} sedadiel`);
  console.log(`Platnosť zostáva: ${result.daysRemaining} dní`);
  
  // Kontrola modulov
  if (result.hasFeature("3d-rendering")) {
    enable3dRenderingModule();
  }
} else {
  console.error(`Licencia je NEPLATNÁ: ${result.failureReason}`);
  if (result.isExpired) {
    showRenewalDialog();
  }
}
```

---

## 2. Web Node-Locking (Hardvérový Odtlačok Prehliadača)

Pre viazanie licencie na konkrétnu klientsku stanicu / prehliadač:

```javascript
import { SymbolonOfflineValidator, generateBrowserFingerprint } from '@symbolon/validator';

// Automatické vygenerovanie stabilného SHA-256 odtlačku stanice
const localFingerprint = await generateBrowserFingerprint();
console.log("Lokálny odtlačok:", localFingerprint); // napr. fp_web_88f921ab04...

// Validácia s kontrolou viazania
const result = await validator.validate(licenseText, localFingerprint);

if (!result.machineMatch) {
  alert("Licencia je viazaná na iný počítač alebo prehliadač!");
}
```

---

## 3. Validácia Produktových Kľúčov LIC-34 (Crockford Base32)

```javascript
import { validateLic34Key } from '@symbolon/validator';

const key = "ABCD-EFGH-JKMN-PQRT-VWXY-1234-5678-9A";
if (validateLic34Key(key)) {
  console.log("Formát kľúča je platný!");
}
```

---

## Vlastnosti a Garancie

- **Nulové závislosti (Zero Dependencies)**: Využíva natívne WebCrypto (`crypto.subtle`), žiadne ťažké externé knižnice (Forge, Webpack polyfills, crypto-js).
- **100% Offline**: Žiadne volania do cloudu, funguje v striktne izolovaných sieťach (air-gapped), na letiskách alebo bez pripojenia k internetu.
- **Kryptografická Integrita**: Každá modifikácia počtu sedadiel, expirácie alebo modulov okamžite zlyhá na kryptografickom podpise.
- **TypeScript Support**: Dodávané s plnohodnotnými `.d.ts` definíciami.

---

## Licencia

Apache-2.0
