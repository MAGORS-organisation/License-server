# Symbolon — Záťažová a Výkonnostná Testovacia Sada (k6 / §10.9)

Oficiálna sada záťažových testov pre **Symbolon Cloud Control Plane** a **On-Premise Relay** vytvorená pre [k6](https://k6.io/).

---

## Cieľové Výkonnostné Metriky (§10.9)

Profily sú navrhnuté a merané proti referenčnému hardvéru (Control Plane: 4 vCPU, 8 GB RAM / Relay: 2 vCPU, 2 GB RAM):

| Profil | Skript | Cieľ (Target KPI) | Prah Zlyhania (Failure Threshold) |
|---|---|---|---|
| **1. Checkout Burst** | `k6/checkout-burst.js` | 500 klientov naraz, $p99 < 250\text{ ms}$, 0 pretečení | akékoľvek prekročenie kapacity = FAIL |
| **2. Heartbeat Steady State** | `k6/heartbeat-steady.js` | 5 000 klientov pri intervale 2 min (~42 rps), $p99 < 50\text{ ms}$ | $p99 > 150\text{ ms}$ alebo chybovosť $> 0.1\%$ |
| **3. Vydávanie Licencií** | `k6/license-issuance.js` | Hybridný podpis (ES256 + ML-DSA-65) $\ge 200\text{ dok/s}$ na jadro | $< 100\text{ dok/s}$ |
| **4. Klientska Verifikácia** | `k6/client-verification.js` | ES256 $< 1\text{ ms}$, ML-DSA-65 $< 3\text{ ms}$ | $> 10\text{ ms}$ |
| **5. Dlhodobý Záťažový Soak Test** | `k6/soak-test.js` | 24-hodinový beh (alebo 1 h skrátený), stabilná pamäť RSS | nárast pamäte bez uvoľnenia (leak) |
| **6. A/B Experimenty & Routing** | `k6/experiments-ab.js` | $p99 < 10\text{ ms}$, nulový drift, rovnomerné rozdelenie | $p > 0.05$ porušenie |
| **7. Relay Súbeh & Probing** | `k6/relay-concurrency.js` | 100 súbežných staníc, $p99 < 200\text{ ms}$, probing $< 25\text{ ms}$ | chybovosť $> 1\%$ |

---

## Príprava a Spustenie

### Inštalácia k6
```bash
# Windows (winget / choco)
winget install k6 --source winget
# alebo
choco install k6

# Linux (Debian / Ubuntu)
sudo gpg -k
sudo gpg --no-default-keyring --keyring /usr/share/keyrings/k6-archive-keyring.gpg --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys C5AD17C747E3415A3642D57D77C6C491D6AC1D69
echo "deb [signed-by=/usr/share/keyrings/k6-archive-keyring.gpg] https://dl.k6.io/deb stable main" | sudo tee /etc/apt/sources.list.d/k6.list
sudo apt-get update && sudo apt-get install k6
```

### Spustenie jednotlivých testov

```bash
# 1. Checkout Burst (500 súbežných klientov)
k6 run -e SERVER_URL=http://localhost:8080 -e LICENSE_KEY=SYM-TEST-BURST-100 k6/checkout-burst.js

# 2. Heartbeat Steady State (5 000 virtuálnych staníc)
k6 run -e SERVER_URL=http://localhost:8080 k6/heartbeat-steady.js

# 3. Priepustnosť Vydávania Licencií (JWS General JSON + ML-DSA-65)
k6 run -e SERVER_URL=http://localhost:8080 k6/license-issuance.js

# 4. Klientska Verifikácia (ES256 + PQC ML-DSA)
k6 run k6/client-verification.js

# 5. Soak Test (dlhodobá stabilita)
k6 run -e SERVER_URL=http://localhost:8080 k6/soak-test.js

# 6. A/B Testovanie a Experimentálny Routing
k6 run -e SERVER_URL=http://localhost:8080 k6/experiments-ab.js

# 7. Relay Súbeh a Zdravotný Monitoring (100 súbežných staníc + probing)
k6 run -e RELAY_URL=http://localhost:5001 k6/relay-concurrency.js
```
