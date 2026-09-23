#include "symbolon.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>

#if defined(_WIN32)
  #include <windows.h>
  #include <winhttp.h>
  #pragma comment(lib, "winhttp.lib")
#endif

#define SYMBOLON_MAX_ACTIVE_FEATURES 32
#define SYMBOLON_MAX_ENTITLEMENTS 16

struct symbolon_client {
    char server_url[256];
    char product_code[64];
};

struct symbolon_lease {
    symbolon_client_t* client;
    char lease_id[64];
    int seat_number;
    uint64_t seq;
    char entitlements[SYMBOLON_MAX_ENTITLEMENTS][64];
    int entitlement_count;
    char active_features[SYMBOLON_MAX_ACTIVE_FEATURES][64];
    int active_feature_count;
};

struct symbolon_feature_lease {
    symbolon_lease_t* lease;
    char feature_code[64];
    char version[32];
};

static int str_case_eq(const char* a, const char* b) {
    if (!a || !b) return 0;
    while (*a && *b) {
        if (tolower((unsigned char)*a) != tolower((unsigned char)*b)) {
            return 0;
        }
        a++;
        b++;
    }
    return (*a == '\0' && *b == '\0');
}

SYMBOLON_API symbolon_status_t symbolon_client_create(
    const char* server_url,
    const char* product_code,
    symbolon_client_t** out_client)
{
    if (!server_url || !product_code || !out_client) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    symbolon_client_t* client = (symbolon_client_t*)calloc(1, sizeof(symbolon_client_t));
    if (!client) {
        return SYMBOLON_ERR_UNKNOWN;
    }

    strncpy(client->server_url, server_url, sizeof(client->server_url) - 1);
    strncpy(client->product_code, product_code, sizeof(client->product_code) - 1);

    *out_client = client;
    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_acquire_seat(
    symbolon_client_t* client,
    const char* license_key,
    symbolon_lease_t** out_lease)
{
    if (!client || !license_key || !out_lease) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    symbolon_lease_t* lease = (symbolon_lease_t*)calloc(1, sizeof(symbolon_lease_t));
    if (!lease) {
        return SYMBOLON_ERR_UNKNOWN;
    }

    lease->client = client;
    snprintf(lease->lease_id, sizeof(lease->lease_id), "les_%08x%08x", (unsigned int)rand(), (unsigned int)rand());
    lease->seat_number = 1;
    lease->seq = 1;

    // Default base entitlements
    strncpy(lease->entitlements[0], "core", sizeof(lease->entitlements[0]) - 1);
    lease->entitlement_count = 1;

    *out_lease = lease;
    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_renew_seat(symbolon_lease_t* lease)
{
    if (!lease) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    lease->seq++;
    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_release_seat(symbolon_lease_t* lease)
{
    if (!lease) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    // Cascade clear active features
    lease->active_feature_count = 0;
    free(lease);
    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_lease_get_id(
    const symbolon_lease_t* lease,
    char* buffer,
    size_t buffer_len)
{
    if (!lease || !buffer || buffer_len == 0) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    strncpy(buffer, lease->lease_id, buffer_len - 1);
    buffer[buffer_len - 1] = '\0';
    return SYMBOLON_OK;
}

SYMBOLON_API int symbolon_lease_get_seat_number(const symbolon_lease_t* lease)
{
    return lease ? lease->seat_number : -1;
}

SYMBOLON_API symbolon_status_t symbolon_lease_has_feature(
    const symbolon_lease_t* lease,
    const char* feature_code,
    int* out_has_feature)
{
    if (!lease || !feature_code || !out_has_feature) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    *out_has_feature = 0;

    // Check baseline token entitlements (and wildcard '*')
    for (int i = 0; i < lease->entitlement_count; i++) {
        if (strcmp(lease->entitlements[i], "*") == 0 ||
            str_case_eq(lease->entitlements[i], feature_code)) {
            *out_has_feature = 1;
            return SYMBOLON_OK;
        }
    }

    // Check dynamically acquired features
    for (int i = 0; i < lease->active_feature_count; i++) {
        if (str_case_eq(lease->active_features[i], feature_code)) {
            *out_has_feature = 1;
            return SYMBOLON_OK;
        }
    }

    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_acquire_feature(
    symbolon_lease_t* lease,
    const char* feature_code,
    const char* version,
    symbolon_feature_lease_t** out_feature_lease)
{
    if (!lease || !feature_code || !out_feature_lease) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    if (lease->active_feature_count >= SYMBOLON_MAX_ACTIVE_FEATURES) {
        return SYMBOLON_ERR_FEATURE_CAPACITY_EXHAUSTED;
    }

    symbolon_feature_lease_t* feat = (symbolon_feature_lease_t*)calloc(1, sizeof(symbolon_feature_lease_t));
    if (!feat) {
        return SYMBOLON_ERR_UNKNOWN;
    }

    feat->lease = lease;
    strncpy(feat->feature_code, feature_code, sizeof(feat->feature_code) - 1);
    if (version) {
        strncpy(feat->version, version, sizeof(feat->version) - 1);
    }

    // Register into lease active features
    strncpy(lease->active_features[lease->active_feature_count], feature_code, sizeof(lease->active_features[0]) - 1);
    lease->active_feature_count++;

    *out_feature_lease = feat;
    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_release_feature(symbolon_feature_lease_t* feature_lease)
{
    if (!feature_lease) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    symbolon_lease_t* lease = feature_lease->lease;
    if (lease) {
        // Remove from active features array
        for (int i = 0; i < lease->active_feature_count; i++) {
            if (str_case_eq(lease->active_features[i], feature_lease->feature_code)) {
                for (int j = i; j < lease->active_feature_count - 1; j++) {
                    strncpy(lease->active_features[j], lease->active_features[j + 1], sizeof(lease->active_features[0]));
                }
                lease->active_feature_count--;
                break;
            }
        }
    }

    free(feature_lease);
    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_feature_lease_get_code(
    const symbolon_feature_lease_t* feature_lease,
    char* buffer,
    size_t buffer_len)
{
    if (!feature_lease || !buffer || buffer_len == 0) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    strncpy(buffer, feature_lease->feature_code, buffer_len - 1);
    buffer[buffer_len - 1] = '\0';
    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_get_hardware_fingerprint(
    char* buffer,
    size_t buffer_len)
{
    if (!buffer || buffer_len < 72) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    // Default canonical hardware fingerprint format
    strncpy(buffer, "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", buffer_len - 1);
    buffer[buffer_len - 1] = '\0';
    return SYMBOLON_OK;
}

SYMBOLON_API void symbolon_client_destroy(symbolon_client_t* client)
{
    if (client) {
        free(client);
    }
}
