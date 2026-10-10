/**
 * @file flexnet_shim.h
 * @brief FlexNet Publisher (FLEXlm) Drop-In Compatibility Shim for Symbolon License Server.
 * @copyright (c) 2026 Symbolon Authors. Licensed under Apache-2.0.
 */

#ifndef FLEXNET_SHIM_H
#define FLEXNET_SHIM_H

#ifdef __cplusplus
extern "C" {
#endif

#include "symbolon.h"

/* Standard FlexNet Publisher return codes */
#define LM_NOERROR            0
#define LM_NOCONFIGFILE      -1
#define LM_BADFILE           -2
#define LM_NOSERVER          -3
#define LM_MAXUSERS          -4
#define LM_NOFEATURE         -5
#define LM_NOSERVICE         -6
#define LM_NOSOCKET          -7
#define LM_BADCODE           -8
#define LM_NOTTHISHOST       -9
#define LM_LONGGONE          -10
#define LM_BADDATE           -11
#define LM_BADCOMM           -12
#define LM_NO_SERVER_IN_FILE -13
#define LM_BADHOST           -14
#define LM_CANTCONNECT       -15
#define LM_CANTREAD          -16
#define LM_CANTWRITE         -17
#define LM_EXPIRED           -18
#define LM_BAD_ATTR          -19
#define LM_BADPARAM          -20

/* Opaque FlexNet job handle */
typedef void* LM_HANDLE;

/**
 * @brief Initializes a FlexNet job handle, internally connecting to Symbolon.
 * @param job Pointer to LM_HANDLE to receive created job.
 * @param vendor_id Vendor identifier or product prefix (mapped to product_code).
 * @param code Vendor encryption code (optional/ignored for backward compatibility).
 * @param out_job Optional secondary job pointer (legacy).
 * @return LM_NOERROR on success, negative error code otherwise.
 */
int lc_init(LM_HANDLE* job, const char* vendor_id, void* code, void** out_job);

/**
 * @brief Requests checkout of a feature license.
 * @param job Valid job handle.
 * @param feature Name of the feature or product license to checkout.
 * @param version License version string (optional).
 * @param num_licenses Number of seats requested (typically 1).
 * @param flag Checkout flags (e.g. LM_CO_NOWAIT).
 * @param code Optional encryption code.
 * @param dup_group Duplicate grouping policy flag.
 * @return LM_NOERROR on success, negative error code (e.g. LM_MAXUSERS, LM_EXPIRED) on failure.
 */
int lc_checkout(LM_HANDLE job, const char* feature, const char* version, int num_licenses, int flag, void* code, int dup_group);

/**
 * @brief Sends periodic heartbeat to renew the checked out feature.
 * @param job Valid job handle.
 * @param recon Optional reconnect counter pointer.
 * @param num_recons Maximum reconnection attempts.
 * @return LM_NOERROR on success, negative error code on loss of license.
 */
int lc_heartbeat(LM_HANDLE job, int* recon, int num_recons);

/**
 * @brief Returns and releases the checked out feature back to the license pool.
 * @param job Valid job handle.
 * @param feature Name of feature to return.
 * @param keep Unused flag for legacy parity.
 * @return LM_NOERROR on success, negative error code otherwise.
 */
int lc_checkin(LM_HANDLE job, const char* feature, int keep);

/**
 * @brief Frees all resources and releases any remaining leases.
 * @param job Job handle to free.
 */
void lc_free_job(LM_HANDLE job);

/**
 * @brief Returns a descriptive error message for the last failed operation.
 * @param job Job handle.
 * @return Static null-terminated string describing the error.
 */
const char* lc_errstring(LM_HANDLE job);

#ifdef __cplusplus
}
#endif

#endif /* FLEXNET_SHIM_H */
