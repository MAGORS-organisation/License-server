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
    SYMBOLON_ERR_FEATURE_DENIED = 6,
    SYMBOLON_ERR_FEATURE_CAPACITY_EXHAUSTED = 7,
    SYMBOLON_ERR_UNKNOWN = 99
} symbolon_status_t;

/** Opaque client handle. */
typedef struct symbolon_client symbolon_client_t;

/** Opaque seat lease handle. */
typedef struct symbolon_lease symbolon_lease_t;

/** Opaque feature lease handle. */
typedef struct symbolon_feature_lease symbolon_feature_lease_t;

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
 * Resolves license server URL from input string or enterprise environment variables
 * (SYMBOLON_LICENSE_SERVER, SYMBOLON_SERVERS).
 * Supports FlexNet "port@host" format and URLs.
 *
 * @param input_or_null Input string to parse, or NULL to read from environment.
 * @param out_buffer Buffer to store the primary resolved server URL (e.g. "http://host:port").
 * @param buffer_size Size of out_buffer in bytes.
 * @return SYMBOLON_OK on success, SYMBOLON_ERR_LICENSE_NOT_FOUND if no server is configured.
 */
SYMBOLON_API symbolon_status_t symbolon_resolve_server(
    const char* input_or_null,
    char* out_buffer,
    size_t buffer_size);

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
 * Checks if a feature code is entitled by the active seat lease or currently acquired.
 *
 * @param lease Active lease handle.
 * @param feature_code Identifier of the feature (e.g. "FEA_SOLVER").
 * @param out_has_feature Pointer to store integer result (1 = entitled/active, 0 = not entitled).
 * @return SYMBOLON_OK on success.
 */
SYMBOLON_API symbolon_status_t symbolon_lease_has_feature(
    const symbolon_lease_t* lease,
    const char* feature_code,
    int* out_has_feature);

/**
 * Dynamically acquires an add-on module / feature seat for an active lease.
 *
 * @param lease Active lease handle.
 * @param feature_code Identifier of the feature to acquire.
 * @param version Optional version string constraint (e.g. "2026.1" or NULL).
 * @param out_feature_lease Pointer to store the acquired feature lease handle.
 * @return SYMBOLON_OK on success, SYMBOLON_ERR_FEATURE_CAPACITY_EXHAUSTED if feature pool is full, SYMBOLON_ERR_FEATURE_DENIED if not permitted.
 */
SYMBOLON_API symbolon_status_t symbolon_acquire_feature(
    symbolon_lease_t* lease,
    const char* feature_code,
    const char* version,
    symbolon_feature_lease_t** out_feature_lease);

/**
 * Releases an acquired feature lease back to the shared pool.
 *
 * @param feature_lease Feature lease handle to release.
 * @return SYMBOLON_OK on success.
 */
SYMBOLON_API symbolon_status_t symbolon_release_feature(symbolon_feature_lease_t* feature_lease);

/**
 * Retrieves the feature code associated with the feature lease.
 *
 * @param feature_lease Valid feature lease handle.
 * @param buffer Output buffer to receive the code.
 * @param buffer_len Size of output buffer.
 * @return SYMBOLON_OK on success.
 */
SYMBOLON_API symbolon_status_t symbolon_feature_lease_get_code(
    const symbolon_feature_lease_t* feature_lease,
    char* buffer,
    size_t buffer_len);

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
 * Retrieves the experiment routing tag (e.g. "exp-a=treatment") associated with the lease, if any.
 *
 * @param lease Active lease handle.
 * @param buffer Output buffer.
 * @param buffer_len Size of output buffer.
 * @return SYMBOLON_OK on success.
 */
SYMBOLON_API symbolon_status_t symbolon_lease_get_experiment_tag(
    const symbolon_lease_t* lease,
    char* buffer,
    size_t buffer_len);

/**
 * Checks whether an algorithm is quantum-safe according to NIST FIPS 203/204/205 standards.
 *
 * @param alg_name Algorithm name (e.g. "ML-DSA-65", "ML-KEM-768", "ES256").
 * @param out_is_safe Output pointer (1 = quantum safe, 0 = classical / vulnerable).
 * @return SYMBOLON_OK on success.
 */
SYMBOLON_API symbolon_status_t symbolon_pqc_is_algorithm_quantum_safe(
    const char* alg_name,
    int* out_is_safe);

/**
 * Checks whether an algorithm meets US CNSA 2.0 requirements.
 *
 * @param alg_name Algorithm name (e.g. "ML-DSA-65", "ML-DSA-87", "ML-KEM-768").
 * @param out_is_compliant Output pointer (1 = CNSA 2.0 compliant, 0 = non-compliant).
 * @return SYMBOLON_OK on success.
 */
SYMBOLON_API symbolon_status_t symbolon_pqc_is_cnsa2_compliant(
    const char* alg_name,
    int* out_is_compliant);

/**
 * Destroys the client handle and frees associated resources.
 */
SYMBOLON_API void symbolon_client_destroy(symbolon_client_t* client);

#ifdef __cplusplus
}

// C++ RAII Wrappers
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

class ScopedFeatureLease {
public:
    explicit ScopedFeatureLease(symbolon_feature_lease_t* feat = nullptr) : feat_(feat) {}
    ~ScopedFeatureLease() {
        if (feat_) {
            symbolon_release_feature(feat_);
            feat_ = nullptr;
        }
    }

    ScopedFeatureLease(ScopedFeatureLease&& other) noexcept : feat_(other.feat_) {
        other.feat_ = nullptr;
    }

    ScopedFeatureLease& operator=(ScopedFeatureLease&& other) noexcept {
        if (this != &other) {
            if (feat_) symbolon_release_feature(feat_);
            feat_ = other.feat_;
            other.feat_ = nullptr;
        }
        return *this;
    }

    ScopedFeatureLease(const ScopedFeatureLease&) = delete;
    ScopedFeatureLease& operator=(const ScopedFeatureLease&) = delete;

    symbolon_feature_lease_t* get() const { return feat_; }
    bool is_valid() const { return feat_ != nullptr; }

private:
    symbolon_feature_lease_t* feat_;
};

} // namespace symbolon
#endif

#endif // SYMBOLON_H
