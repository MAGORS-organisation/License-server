/**
 * @file symbolon.h
 * @brief Official C/C++ SDK for the Symbolon Floating & Enterprise License Server.
 * @copyright (c) 2026 Symbolon Authors. Licensed under Apache-2.0.
 */

#ifndef SYMBOLON_H
#define SYMBOLON_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32) || defined(__CYGWIN__)
  #ifdef SYMBOLON_EXPORTS
    #define SYMBOLON_API __declspec(dllexport)
  #else
    #define SYMBOLON_API
  #endif
#else
  #define SYMBOLON_API __attribute__((visibility("default")))
#endif

/** Status codes returned by Symbolon SDK functions. */
typedef enum {
    SYMBOLON_OK = 0,
    SYMBOLON_ERR_INVALID_ARGUMENT = 1,
    SYMBOLON_ERR_NETWORK = 2,
    SYMBOLON_ERR_CAPACITY_EXHAUSTED = 3,
    SYMBOLON_ERR_LICENSE_NOT_FOUND = 4,
    SYMBOLON_ERR_EXPIRED = 5,
    SYMBOLON_ERR_UNKNOWN = 99
} symbolon_status_t;

/** Opaque client handle. */
typedef struct symbolon_client symbolon_client_t;

/** Opaque seat lease handle. */
typedef struct symbolon_lease symbolon_lease_t;

/**
 * Creates and initializes a Symbolon client.
 *
 * @param server_url Base URL of ControlPlane or Relay (e.g. "http://localhost:8080").
 * @param product_code Product identifier (e.g. "cad-pro").
 * @param out_client Pointer to store the created client instance.
 * @return SYMBOLON_OK on success.
 */
SYMBOLON_API symbolon_status_t symbolon_client_create(
    const char* server_url,
    const char* product_code,
    symbolon_client_t** out_client);

/**
 * Acquires a floating concurrent seat lease.
 *
 * @param client Valid client handle.
 * @param license_key Crockford Base32 license key (e.g. "SYM-XXXX-...").
 * @param out_lease Pointer to store the acquired seat lease.
 * @return SYMBOLON_OK on success, SYMBOLON_ERR_CAPACITY_EXHAUSTED if pool is full.
 */
SYMBOLON_API symbolon_status_t symbolon_acquire_seat(
    symbolon_client_t* client,
    const char* license_key,
    symbolon_lease_t** out_lease);

/**
 * Renews an active seat lease (heartbeat).
 *
 * @param lease Active lease handle.
 * @return SYMBOLON_OK on success.
 */
SYMBOLON_API symbolon_status_t symbolon_renew_seat(symbolon_lease_t* lease);

/**
 * Releases the seat lease back to the pool and destroys the lease handle.
 *
 * @param lease Lease handle to release.
 * @return SYMBOLON_OK on success.
 */
SYMBOLON_API symbolon_status_t symbolon_release_seat(symbolon_lease_t* lease);

/**
 * Retrieves the Lease ID string.
 */
SYMBOLON_API symbolon_status_t symbolon_lease_get_id(
    const symbolon_lease_t* lease,
    char* buffer,
    size_t buffer_len);

/**
 * Retrieves the assigned seat index (e.g. 1 to N).
 */
SYMBOLON_API int symbolon_lease_get_seat_number(const symbolon_lease_t* lease);

/**
 * Computes canonical SHA-256 hardware fingerprint of the current machine.
 *
 * @param buffer Output buffer (min 72 bytes recommended).
 * @param buffer_len Size of output buffer.
 * @return SYMBOLON_OK on success.
 */
SYMBOLON_API symbolon_status_t symbolon_get_hardware_fingerprint(
    char* buffer,
    size_t buffer_len);

/**
 * Destroys the client handle and frees associated resources.
 */
SYMBOLON_API void symbolon_client_destroy(symbolon_client_t* client);

#ifdef __cplusplus
}

// C++ RAII Wrapper
namespace symbolon {

class ScopedLease {
public:
    explicit ScopedLease(symbolon_lease_t* lease = nullptr) : lease_(lease) {}
    ~ScopedLease() {
        if (lease_) {
            symbolon_release_seat(lease_);
            lease_ = nullptr;
        }
    }

    ScopedLease(ScopedLease&& other) noexcept : lease_(other.lease_) {
        other.lease_ = nullptr;
    }

    ScopedLease& operator=(ScopedLease&& other) noexcept {
        if (this != &other) {
            if (lease_) symbolon_release_seat(lease_);
            lease_ = other.lease_;
            other.lease_ = nullptr;
        }
        return *this;
    }

    ScopedLease(const ScopedLease&) = delete;
    ScopedLease& operator=(const ScopedLease&) = delete;

    symbolon_lease_t* get() const { return lease_; }
    bool is_valid() const { return lease_ != nullptr; }

private:
    symbolon_lease_t* lease_;
};

} // namespace symbolon
#endif

#endif // SYMBOLON_H
