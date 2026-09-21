#include <stdio.h>
#include "symbolon.h"

int main(void)
{
    printf("=== Symbolon C/C++ SDK Example ===\n");

    // 1. Check local hardware fingerprint
    char fp[72];
    if (symbolon_get_hardware_fingerprint(fp, sizeof(fp)) == SYMBOLON_OK) {
        printf("Local HW Fingerprint: %s\n", fp);
    }

    // 2. Initialize Symbolon Client
    symbolon_client_t* client = NULL;
    symbolon_status_t status = symbolon_client_create("http://localhost:8080", "cad-pro", &client);
    if (status != SYMBOLON_OK) {
        printf("Failed to create client: %d\n", status);
        return 1;
    }

    // 3. Acquire floating seat
    symbolon_lease_t* lease = NULL;
    status = symbolon_acquire_seat(client, "SYM-9ABC-DEF2-3456-7890", &lease);
    if (status == SYMBOLON_OK) {
        char lease_id[64];
        symbolon_lease_get_id(lease, lease_id, sizeof(lease_id));
        printf("Seat successfully acquired! Lease ID: %s, Seat #%d\n",
               lease_id, symbolon_lease_get_seat_number(lease));

        // 4. Renew seat (heartbeat)
        symbolon_renew_seat(lease);
        printf("Heartbeat renewal sent.\n");

        // 5. Release seat
        symbolon_release_seat(lease);
        printf("Seat released back to pool.\n");
    } else if (status == SYMBOLON_ERR_CAPACITY_EXHAUSTED) {
        printf("Capacity exhausted: No floating seats available.\n");
    }

    // 6. Cleanup client
    symbolon_client_destroy(client);
    printf("Done.\n");
    return 0;
}
