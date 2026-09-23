# Symbolon C / C++ SDK

Official C99 / C++17 client library for the **Symbolon Floating & Enterprise License Server**.

## Kompilácia

### GCC / Clang (Linux / macOS / MinGW):
```bash
gcc -std=c99 -Iinclude src/symbolon.c examples/main.c -o symbolon_example
./symbolon_example
```

### MSVC (Windows):
```cmd
cl /std:c11 /Iinclude src\symbolon.c examples\main.c /Fe:symbolon_example.exe
symbolon_example.exe
```

## Použitie v C++ (RAII ScopedLease & ScopedFeatureLease)

```cpp
#include "symbolon.h"
#include <iostream>

int main() {
    symbolon_client_t* client = nullptr;
    symbolon_client_create("http://localhost:8080", "cad-pro", &client);

    symbolon_lease_t* raw_lease = nullptr;
    if (symbolon_acquire_seat(client, "SYM-9ABC-DEF2-3456-7890", &raw_lease) == SYMBOLON_OK) {
        symbolon::ScopedLease lease(raw_lease);

        // Overenie oprávnenia funkcie
        int has_fea = 0;
        symbolon_lease_has_feature(lease.get(), "FEA_SOLVER", &has_fea);

        // Dynamická alokácia modulu cez RAII ScopedFeatureLease
        symbolon_feature_lease_t* raw_feat = nullptr;
        if (symbolon_acquire_feature(lease.get(), "FEA_SOLVER", "2026.1", &raw_feat) == SYMBOLON_OK) {
            symbolon::ScopedFeatureLease feat(raw_feat);

            std::cout << "FEA Solver beží..." << std::endl;
            // Po ukončení scope sa modul automaticky uvoľní cez deštruktor ScopedFeatureLease!
        }

        // Po ukončení scope sa hlavné sedadlo automaticky uvoľní cez ScopedLease!
    }

    symbolon_client_destroy(client);
    return 0;
}
```
