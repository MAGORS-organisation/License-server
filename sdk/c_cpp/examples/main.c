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
        printf("✓ Seat successfully acquired! Lease ID: %s, Seat #%d\n",
               lease_id, symbolon_lease_get_seat_number(lease));

        // 4. Check feature entitlements
        int has_core = 0;
        symbolon_lease_has_feature(lease, "core", &has_core);
        printf("Has 'core' entitlement: %s\n", has_core ? "YES" : "NO");

        int has_fea = 0;
        symbolon_lease_has_feature(lease, "FEA_SOLVER", &has_fea);
        printf("Has 'FEA_SOLVER' before acquire: %s\n", has_fea ? "YES" : "NO");

        // 5. Dynamically acquire add-on feature
        symbolon_feature_lease_t* feat_lease = NULL;
        status = symbolon_acquire_feature(lease, "FEA_SOLVER", "2026.1", &feat_lease);
        if (status == SYMBOLON_OK) {
            char feat_code[64];
            symbolon_feature_lease_get_code(feat_lease, feat_code, sizeof(feat_code));
            printf("✓ Dynamically acquired feature module: %s\n", feat_code);

            symbolon_lease_has_feature(lease, "FEA_SOLVER", &has_fea);
            printf("Has 'FEA_SOLVER' during execution: %s\n", has_fea ? "YES" : "NO");

            // Execute FEA solver kernel...
            printf("Running solver simulation calculations...\n");

            // 6. Release feature module
            symbolon_release_feature(feat_lease);
            printf("Feature module released back to pool.\n");

            symbolon_lease_has_feature(lease, "FEA_SOLVER", &has_fea);
            printf("Has 'FEA_SOLVER' after release: %s\n", has_fea ? "YES" : "NO");
        } else {
            printf("Failed to acquire feature: error code %d\n", status);
        }

        // 7. Renew seat (heartbeat)
        symbolon_renew_seat(lease);
        printf("Heartbeat renewal sent.\n");

        // 8. Release seat
        symbolon_release_seat(lease);
        printf("Seat released back to pool.\n");
    } else if (status == SYMBOLON_ERR_CAPACITY_EXHAUSTED) {
        printf("Capacity exhausted: No floating seats available.\n");
    }

    // 9. Metered Pay-As-You-Go Token Consumption
    printf("\n--- Metered Pay-As-You-Go Token Usage ---\n");
    symbolon_token_scope_t* token_scope = NULL;
    status = symbolon_tokens_begin_scope(client, "wlt_enterprise", "ai_inference", 100.0, &token_scope);
    if (status == SYMBOLON_OK) {
        printf("✓ Reserved 100 credits in auto-rollback scope.\n");
        // Simulate operation consuming 75 credits
        double new_balance = 0.0;
        symbolon_tokens_scope_commit(token_scope, 75.0, &new_balance);
        printf("✓ Committed 75 credits. Wallet balance settled: %.2f\n", new_balance);
        symbolon_tokens_scope_destroy(token_scope);
    }

    // 10. Cleanup client
    symbolon_client_destroy(client);
    printf("Done.\n");
    return 0;
}
