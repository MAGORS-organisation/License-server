/**
 * @file flexnet_shim.c
 * @brief Implementation of FlexNet Publisher compatibility drop-in shim.
 * @copyright (c) 2026 Symbolon Authors. Licensed under Apache-2.0.
 */

#include "flexnet_shim.h"
#include <stdlib.h>
#include <string.h>

struct flexnet_job {
    symbolon_client_t* symbolon;
    char last_feature[64];
    int error_code;
    char error_msg[256];
};

static int map_symbolon_to_flexnet(symbolon_error_t err) {
    switch (err) {
        case SYMBOLON_OK:
            return LM_NOERROR;
        case SYMBOLON_ERR_CAPACITY_LIMIT:
        case SYMBOLON_ERR_DENIED:
            return LM_MAXUSERS;
        case SYMBOLON_ERR_EXPIRED:
            return LM_EXPIRED;
        case SYMBOLON_ERR_NETWORK:
        case SYMBOLON_ERR_OFFLINE:
            return LM_CANTCONNECT;
        case SYMBOLON_ERR_INVALID_PARAM:
            return LM_BADPARAM;
        default:
            return LM_BADCODE;
    }
}

int lc_init(LM_HANDLE* job, const char* vendor_id, void* code, void** out_job) {
    (void)code;
    (void)out_job;
    if (!job) return LM_BADPARAM;

    struct flexnet_job* fjob = (struct flexnet_job*)calloc(1, sizeof(struct flexnet_job));
    if (!fjob) return LM_BADPARAM;

    symbolon_config_t config;
    memset(&config, 0, sizeof(config));
    config.product_code = vendor_id ? vendor_id : "FLEXNET-LEGACY";
    config.license_key = getenv("SYMBOLON_LICENSE_KEY");
    config.server_url = getenv("SYMBOLON_LICENSE_SERVER");
    config.agent_ipc_port = 8189;

    symbolon_error_t err = symbolon_client_create(&config, &fjob->symbolon);
    if (err != SYMBOLON_OK) {
        free(fjob);
        return map_symbolon_to_flexnet(err);
    }

    *job = (LM_HANDLE)fjob;
    return LM_NOERROR;
}

int lc_checkout(LM_HANDLE job, const char* feature, const char* version, int num_licenses, int flag, void* code, int dup_group) {
    (void)version;
    (void)num_licenses;
    (void)flag;
    (void)code;
    (void)dup_group;

    if (!job || !feature) return LM_BADPARAM;
    struct flexnet_job* fjob = (struct flexnet_job*)job;

    strncpy(fjob->last_feature, feature, sizeof(fjob->last_feature) - 1);

    symbolon_lease_t lease;
    symbolon_error_t err = symbolon_checkout(fjob->symbolon, &lease);
    fjob->error_code = map_symbolon_to_flexnet(err);

    if (fjob->error_code != LM_NOERROR) {
        strncpy(fjob->error_msg, symbolon_get_last_error(fjob->symbolon), sizeof(fjob->error_msg) - 1);
    } else {
        fjob->error_msg[0] = '\0';
    }

    return fjob->error_code;
}

int lc_heartbeat(LM_HANDLE job, int* recon, int num_recons) {
    (void)recon;
    (void)num_recons;
    if (!job) return LM_BADPARAM;
    struct flexnet_job* fjob = (struct flexnet_job*)job;

    symbolon_error_t err = symbolon_renew(fjob->symbolon, NULL);
    fjob->error_code = map_symbolon_to_flexnet(err);
    return fjob->error_code;
}

int lc_checkin(LM_HANDLE job, const char* feature, int keep) {
    (void)feature;
    (void)keep;
    if (!job) return LM_BADPARAM;
    struct flexnet_job* fjob = (struct flexnet_job*)job;

    symbolon_error_t err = symbolon_release(fjob->symbolon, NULL);
    fjob->error_code = map_symbolon_to_flexnet(err);
    return fjob->error_code;
}

void lc_free_job(LM_HANDLE job) {
    if (!job) return;
    struct flexnet_job* fjob = (struct flexnet_job*)job;
    if (fjob->symbolon) {
        symbolon_client_destroy(fjob->symbolon);
    }
    free(fjob);
}

const char* lc_errstring(LM_HANDLE job) {
    if (!job) return "Invalid license job handle";
    struct flexnet_job* fjob = (struct flexnet_job*)job;

    if (fjob->error_msg[0]) return fjob->error_msg;

    switch (fjob->error_code) {
        case LM_NOERROR: return "No error (License verified successfully)";
        case LM_MAXUSERS: return "All available floating licenses are in use (Capacity exhausted)";
        case LM_EXPIRED: return "License key or feature entitlement has expired";
        case LM_CANTCONNECT: return "Cannot connect to license server or local Symbolon Agent";
        case LM_BADPARAM: return "Invalid license parameter passed to FlexNet API";
        default: return "License checkout or verification failed";
    }
}
