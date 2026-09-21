# Symbolon Python SDK

Official Python SDK for the **Symbolon Floating & Enterprise License Server**.

## Inštalácia

```bash
pip install .
```

## Použitie

```python
from symbolon import SymbolonClient

# Inicializácia klienta
client = SymbolonClient(
    server_url="http://license.acme.corp:8080",
    product_code="cad-pro",
    heartbeat_interval=30.0,
    jitter_factor=0.10 # ±10% rozptyl pre zamedzenie thundering herd
)

# Získanie plávajúceho sedadla cez Python Context Manager
with client.acquire_seat("SYM-9ABC-DEF2-3456-7890", features=["cad-export"]) as lease:
    print(f"Sedadlo pridelené! Lease ID: {lease.lease_id}, Číslo: {lease.seat_number}")
    
    # Výpočtové jadro vašej aplikácie...
    # Na pozadí automaticky prebieha heartbeat.

# Po opustení with bloku sa sedadlo automaticky uvoľní späť do fondu.
```
