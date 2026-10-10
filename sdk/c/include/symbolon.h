/**
 * @file symbolon.h
 * @brief Official C/C++ Native SDK Header for Symbolon Enterprise Floating License Server.
 * @copyright (c) 2026 Symbolon Authors. Licensed under Apache-2.0.
 */

#ifndef SYMBOLON_H
#define SYMBOLON_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stddef.h>
#include <stdint.h>

#define SYMBOLON_VERSION_MAJOR 2
#define SYMBOLON_VERSION_MINOR 4
#define SYMBOLON_VERSION_PATCH 0

/**
 * @brief Return and error codes for Symbolon native operations.
 */
typedef enum {
    SYMBOLON_OK                  = 0,
    SYMBOLON_ERR_GENERIC         = -1,
    SYMBOLON_ERR_INVALID_PARAM   = -2,
    SYMBOLON_ERR_NETWORK         = -3,
    SYMBOLON_ERR_DENIED          = -4,
    SYMBOLON_ERR_EXPIRED         = -5,
    SYMBOLON_ERR_NOT_FOUND       = -6,
    SYMBOLON_ERR_OFFLINE         = -7,
    SYMBOLON_ERR_CAPACITY_LIMIT  = -8
} symbolon_error_t;

/**
 * @brief Current status of the floating lease held by the client.
 */
typedef enum {
    SYMBOLON_STATUS_IDLE     = 0,
    SYMBOLON_STATUS_ACTIVE   = 1,
    SYMBOLON_STATUS_GRACE    = 2,
    SYMBOLON_STATUS_BORROWED = 3
} symbolon_state_t;

/**
 * @brief Active lease information structure.
 */
typedef struct {
    char lease_id[64];
    char license_key[64];
    char product_code[64];
    int seat_number;
    int64_t expires_at_unix;
    int is_borrowed;
    char token[2048];
} symbolon_lease_t;

/**
 * @brief Client initialization configuration.
 */
typedef struct {
    const char* server_url;      /* e.g. "http://localhost:8080" or NULL for agent IPC */
    const char* license_key;     /* e.g. "SYM-PRO-1234-ABCD-EFGH-IJKL" */
    const char* product_code;    /* e.g. "CAD-ENTERPRISE" */
    int agent_ipc_port;          /* default: 8189, 0 to use default */
    int heartbeat_seconds;       /* default: 120 */
    int timeout_ms;              /* default: 5000 */
} symbolon_config_t;

/**
 * @brief Opaque client handle.
 */
typedef struct symbolon_client symbolon_client_t;

/**
 * @brief Initializes a new Symbolon client instance.
 * @param config Pointer to client configuration.
 * @param out_client Address of pointer to receive the created handle.
 * @return SYMBOLON_OK on success, negative error code otherwise.
 */
symbolon_error_t symbolon_client_create(const symbolon_config_t* config, symbolon_client_t** out_client);

/**
 * @brief Requests a floating seat checkout from the license pool.
 * @param client Valid client handle.
 * @param out_lease Pointer to receive acquired lease details.
 * @return SYMBOLON_OK on success, negative error code otherwise.
 */
symbolon_error_t symbolon_checkout(symbolon_client_t* client, symbolon_lease_t* out_lease);

/**
 * @brief Renews an active floating seat lease (heartbeat).
 * @param client Valid client handle.
 * @param lease_id Unique ID of the lease to renew, or NULL to renew currently held lease.
 * @return SYMBOLON_OK on success, negative error code otherwise.
 */
symbolon_error_t symbolon_renew(symbolon_client_t* client, const char* lease_id);

/**
 * @brief Releases a floating seat lease back to the server pool.
 * @param client Valid client handle.
 * @param lease_id Unique ID of the lease to release, or NULL for current lease.
 * @return SYMBOLON_OK on success, negative error code otherwise.
 */
symbolon_error_t symbolon_release(symbolon_client_t* client, const char* lease_id);

/**
 * @brief Borrows a floating seat for offline work for a given number of days.
 * @param client Valid client handle.
 * @param lease_id Unique ID of the lease to borrow, or NULL for current lease.
 * @param days Duration in days (1 to 30).
 * @return SYMBOLON_OK on success, negative error code otherwise.
 */
symbolon_error_t symbolon_borrow(symbolon_client_t* client, const char* lease_id, int days);

/**
 * @brief Queries current client state.
 * @param client Valid client handle.
 * @param out_state Pointer to receive current state.
 * @return SYMBOLON_OK on success, negative error code otherwise.
 */
symbolon_error_t symbolon_get_state(symbolon_client_t* client, symbolon_state_t* out_state);

/**
 * @brief Returns the last recorded error message.
 * @param client Valid client handle.
 * @return Pointer to null-terminated error string.
 */
const char* symbolon_get_last_error(symbolon_client_t* client);

/**
 * @brief Destroys the client handle and frees associated resources.
 * @param client Client handle to destroy.
 */
void symbolon_client_destroy(symbolon_client_t* client);

#ifdef __cplusplus
}
#endif

#endif /* SYMBOLON_H */
