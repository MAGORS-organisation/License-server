#include "symbolon.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
  #include <windows.h>
  #include <winhttp.h>
  #pragma comment(lib, "winhttp.lib")
#endif

struct symbolon_client {
    char server_url[256];
    char product_code[64];
};

struct symbolon_lease {
    symbolon_client_t* client;
    char lease_id[64];
    int seat_number;
    uint64_t seq;
};

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
