# Symbolon — Nasadenie a Orchestrácia (Deployment Guide)

Tento adresár obsahuje kompletnú infraštruktúru a orchestráciu pre nasadenie systému **Symbolon** v rôznych prostrediach:

| Nástroj / Komponent | Cesta | Účel |
|---|---|---|
| **Docker Compose** | [`docker-compose.yml`](docker-compose.yml) | Lokálne vývojové a testovacie prostredie (PostgreSQL 17, ControlPlane, Relay, Prometheus, Grafana). |
| **Docker Kontajnery** | [`docker/`](docker/) | Produkčné multi-stage Dockerfiles pre ControlPlane a Relay (.NET 10). |
| **Kubernetes Helm Chart** | [`helm/symbolon/`](helm/symbolon/) | Cloudové nasadenie pre Kubernetes klastre (EKS, GKE, AKS, OpenShift). |
| **Grafana Dashboard** | [`grafana/`](grafana/) | Pripravený dashboard a konfigurácia automatického provisioning dátového zdroja Prometheus. |
| **Prometheus Config** | [`prometheus/`](prometheus/) | Konfigurácia zberu metrík z ControlPlane na porte 8080. |
| **Systemd Service** | [`systemd/`](systemd/) | Zabezpečený Linux service unit pre beh `Symbolon.Relay` na virtuálnych alebo fyzických serveroch. |

---

## 1. Rýchle Spustenie cez Docker Compose

Spustí kompletný stack jedným príkazom:

```bash
docker compose up -d
```

### Dostupné služby:
- **ControlPlane API:** `http://localhost:8080`
  - Health check: `http://localhost:8080/health/ready`
  - Prometheus metriky: `http://localhost:8080/metrics`
  - OpenAPI 3.1: `http://localhost:8080/openapi/v1.json`
- **Relay Server:** `http://localhost:8081`
- **Prometheus:** `http://localhost:9090`
- **Grafana:** `http://localhost:3000`
  - Predvolené prihlasovacie údaje: Používateľ `admin`, Heslo `admin`
  - Dashboard je automaticky nahraný v priečinku `Symbolon` -> *Symbolon Licensing Dashboard*.

Ukončenie stacku:
```bash
docker compose down
```

---

## 2. Zostavenie Docker Kontajnerov

### ControlPlane:
```bash
docker build -f docker/Dockerfile.controlplane -t symbolon-controlplane:1.0.0 ..
```

### Relay Server:
```bash
docker build -f docker/Dockerfile.relay -t symbolon-relay:1.0.0 ..
```

Oba kontajnery bežia pod neprivilegovaným používateľom (`$APP_UID`, UID 1654) v súlade s najvyššími bezpečnostnými štandardmi.

---

## 3. Kubernetes Nasadenie pomocou Helm Chartu

Chart v `helm/symbolon/` podporuje plnú konfiguráciu produkčného klastra.

### Inštalácia:
```bash
helm upgrade --install symbolon ./helm/symbolon \
  --namespace symbolon --create-namespace \
  --set database.host="postgres-cluster.database.svc" \
  --set database.password="vase_bezpecne_heslo" \
  --set ingress.enabled=true \
  --set ingress.hosts[0].host="license.vasadomena.sk"
```

### Kľúčové vlastnosti chartu:
- **Autoscaling (HPA):** Automatické škálovanie replík (min: 2, max: 10) podľa vyťaženia CPU (75%) a RAM (80%).
- **Sondy (Probes):**
  - Liveness probe: `/health/live`
  - Readiness probe: `/health/ready` (overuje aktívne spojenie s DB)
- **Hardening:** Read-only root filesystem, zhodenie všetkých Linux capabilities, runAsNonRoot.
- **Prometheus Discovery:** Automatické anotácie `prometheus.io/scrape: "true"`.

---

## 4. Inštalácia Relay ako Linux Systemd Služby

Pre on-premise nasadenie u zákazníka (bare-metal alebo virtuálny server):

1. Skopírujte publikovaný binár `Symbolon.Relay` do `/opt/symbolon/relay/`.
2. Vytvorte systémového používateľa:
   ```bash
   sudo useradd -r -s /bin/false symbolon
   sudo mkdir -p /var/lib/symbolon
   sudo chown -R symbolon:symbolon /var/lib/symbolon /opt/symbolon/relay
   ```
3. Skopírujte unit súbor:
   ```bash
   sudo cp systemd/symbolon-relay.service /etc/systemd/system/
   sudo systemctl daemon-reload
   sudo systemctl enable --now symbolon-relay
   ```
4. Kontrola stavu:
   ```bash
   sudo systemctl status symbolon-relay
   ```
