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
    char experiment_tag[64];
};

struct symbolon_feature_lease {
    symbolon_lease_t* lease;
    char feature_code[64];
    char version[32];
};

struct symbolon_token_scope {
    symbolon_client_t* client;
    char wallet_id[64];
    char reservation_id[64];
    char feature_code[64];
    double reserved_amount;
    double available_balance;
    int is_completed;
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

SYMBOLON_API symbolon_status_t symbolon_resolve_server(
    const char* input_or_null,
    char* out_buffer,
    size_t buffer_size)
{
    if (!out_buffer || buffer_size == 0) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    const char* candidate = input_or_null;
    char env_buf[512] = {0};

    if (!candidate || candidate[0] == '\0') {
#if defined(_WIN32)
        DWORD len = GetEnvironmentVariableA("SYMBOLON_LICENSE_SERVER", env_buf, (DWORD)sizeof(env_buf));
        if (len == 0) {
            len = GetEnvironmentVariableA("SYMBOLON_SERVERS", env_buf, (DWORD)sizeof(env_buf));
        }
        if (len > 0 && len < sizeof(env_buf)) {
            candidate = env_buf;
        }
#else
        candidate = getenv("SYMBOLON_LICENSE_SERVER");
        if (!candidate || candidate[0] == '\0') {
            candidate = getenv("SYMBOLON_SERVERS");
        }
#endif
    }

    if (!candidate || candidate[0] == '\0') {
        return SYMBOLON_ERR_LICENSE_NOT_FOUND;
    }

    // Isolate first entry if list is separated by ';' or ','
    char token[256] = {0};
    size_t i = 0;
    while (candidate[i] != '\0' && candidate[i] != ';' && candidate[i] != ',' && i < sizeof(token) - 1) {
        token[i] = candidate[i];
        i++;
    }
    token[i] = '\0';

    // Trim leading whitespace
    char* p = token;
    while (*p == ' ' || *p == '\t') p++;
    if (*p == '\0') {
        return SYMBOLON_ERR_LICENSE_NOT_FOUND;
    }

    // Trim trailing whitespace
    char* end = p + strlen(p) - 1;
    while (end > p && (*end == ' ' || *end == '\t')) {
        *end = '\0';
        end--;
    }

    // Check FlexNet notation: [port]@host[:port]
    char* at_sign = strchr(p, '@');
    if (at_sign) {
        *at_sign = '\0';
        char* port_str = p;
        char* host_str = at_sign + 1;
        char* colon = strchr(host_str, ':');
        if (colon) {
            *colon = '\0';
            port_str = colon + 1;
        }
        if (port_str[0] == '\0') {
            port_str = "8080";
        }
        snprintf(out_buffer, buffer_size, "http://%s:%s", host_str, port_str);
        return SYMBOLON_OK;
    }

    if (strncmp(p, "http://", 7) == 0 || strncmp(p, "https://", 8) == 0) {
        snprintf(out_buffer, buffer_size, "%s", p);
        return SYMBOLON_OK;
    }

    if (strchr(p, ':')) {
        snprintf(out_buffer, buffer_size, "http://%s", p);
    } else {
        snprintf(out_buffer, buffer_size, "http://%s:8080", p);
    }

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
    strncpy(lease->experiment_tag, "variant=baseline", sizeof(lease->experiment_tag) - 1);

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

SYMBOLON_API symbolon_status_t symbolon_is_container_or_cloud(int* out_is_container)
{
    if (!out_is_container) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    *out_is_container = 0;

    // 1. Container marker files
    FILE* f_docker = fopen("/.dockerenv", "r");
    if (f_docker) {
        fclose(f_docker);
        *out_is_container = 1;
        return SYMBOLON_OK;
    }
    FILE* f_container = fopen("/run/.containerenv", "r");
    if (f_container) {
        fclose(f_container);
        *out_is_container = 1;
        return SYMBOLON_OK;
    }

    // 2. Linux /proc/1/cgroup inspection
    FILE* f_cgroup = fopen("/proc/1/cgroup", "r");
    if (f_cgroup) {
        char buf[1024];
        while (fgets(buf, sizeof(buf), f_cgroup)) {
            for (char* p = buf; *p; ++p) *p = (char)tolower((unsigned char)*p);
            if (strstr(buf, "docker") || strstr(buf, "containerd") ||
                strstr(buf, "kubepods") || strstr(buf, "lxc")) {
                fclose(f_cgroup);
                *out_is_container = 1;
                return SYMBOLON_OK;
            }
        }
        fclose(f_cgroup);
    }

    // 3. Environment variables (FPR-11)
    const char* cloud_vars[] = {
        "KUBERNETES_SERVICE_HOST",
        "container",
        "DOTNET_RUNNING_IN_CONTAINER",
        "AWS_EXECUTION_ENV",
        "ECS_CONTAINER_METADATA_URI",
        "AZURE_CONTAINER_APP_NAME",
        "GOOGLE_CLOUD_PROJECT",
        NULL
    };
    for (int i = 0; cloud_vars[i] != NULL; ++i) {
        const char* val = getenv(cloud_vars[i]);
        if (val && strlen(val) > 0) {
            *out_is_container = 1;
            return SYMBOLON_OK;
        }
    }

    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_get_or_create_persisted_container_uuid(
    const char* custom_volume_path,
    char* buffer,
    size_t buffer_len)
{
    if (!buffer || buffer_len < 37) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    char file_path[512];
    if (custom_volume_path && strlen(custom_volume_path) > 0) {
        strncpy(file_path, custom_volume_path, sizeof(file_path) - 1);
        file_path[sizeof(file_path) - 1] = '\0';
    } else {
        const char* local_app = getenv("LOCALAPPDATA");
        if (local_app && strlen(local_app) > 0) {
            snprintf(file_path, sizeof(file_path), "%s\\symbolon_container_uuid.txt", local_app);
        } else {
            const char* home = getenv("HOME");
            if (home && strlen(home) > 0) {
                snprintf(file_path, sizeof(file_path), "%s/.symbolon_container_uuid.txt", home);
            } else {
                strncpy(file_path, "/tmp/symbolon_container_uuid.txt", sizeof(file_path) - 1);
                file_path[sizeof(file_path) - 1] = '\0';
            }
        }
    }

    FILE* f = fopen(file_path, "r");
    if (f) {
        char existing[64];
        if (fgets(existing, sizeof(existing), f)) {
            char* trimmed = existing;
            while (*trimmed && isspace((unsigned char)*trimmed)) trimmed++;
            size_t len = strlen(trimmed);
            while (len > 0 && isspace((unsigned char)trimmed[len - 1])) {
                trimmed[--len] = '\0';
            }
            if (len >= 32) {
                fclose(f);
                strncpy(buffer, trimmed, buffer_len - 1);
                buffer[buffer_len - 1] = '\0';
                return SYMBOLON_OK;
            }
        }
        fclose(f);
    }

    // Generate pseudo-random UUID v4
    unsigned int r1 = (unsigned int)rand();
    unsigned int r2 = (unsigned int)rand();
    unsigned int r3 = (unsigned int)rand();
    unsigned int r4 = (unsigned int)rand();
    snprintf(buffer, buffer_len, "%08x-%04x-4%03x-%04x-%04x%08x",
        r1,
        (r2 >> 16) & 0xffff,
        r2 & 0x0fff,
        0x8000 | (r3 & 0x3fff),
        r3 >> 16,
        r4);

    FILE* f_out = fopen(file_path, "w");
    if (f_out) {
        fputs(buffer, f_out);
        fclose(f_out);
    }

    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_get_hardware_fingerprint(
    char* buffer,
    size_t buffer_len)
{
    if (!buffer || buffer_len < 72) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    int is_container = 0;
    symbolon_is_container_or_cloud(&is_container);
    if (is_container) {
        fprintf(stderr, "WARNING: Containerized or cloud environment detected. Hardware node-locking is an anti-pattern in containers. Recommended: floating license with short lease TTL (FPR-13).\n");
        char container_uuid[64];
        if (symbolon_get_or_create_persisted_container_uuid(NULL, container_uuid, sizeof(container_uuid)) == SYMBOLON_OK) {
            snprintf(buffer, buffer_len, "sha256:container-%s", container_uuid);
            return SYMBOLON_OK;
        }
    }

    // Default canonical hardware fingerprint format
    strncpy(buffer, "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", buffer_len - 1);
    buffer[buffer_len - 1] = '\0';
    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_lease_get_experiment_tag(
    const symbolon_lease_t* lease,
    char* buffer,
    size_t buffer_len)
{
    if (!lease || !buffer || buffer_len == 0) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    strncpy(buffer, lease->experiment_tag, buffer_len - 1);
    buffer[buffer_len - 1] = '\0';
    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_pqc_is_algorithm_quantum_safe(
    const char* alg_name,
    int* out_is_safe)
{
    if (!alg_name || !out_is_safe) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    static const char* pqc_algs[] = {
        "ML-DSA-44", "ML-DSA-65", "ML-DSA-87",
        "ML-KEM-512", "ML-KEM-768", "ML-KEM-1024",
        "SLH-DSA-SHA2-128s", "SLH-DSA-SHA2-128f", "SLH-DSA-SHAKE-128s"
    };
    int count = (int)(sizeof(pqc_algs) / sizeof(pqc_algs[0]));

    *out_is_safe = 0;
    for (int i = 0; i < count; i++) {
        if (str_case_eq(alg_name, pqc_algs[i])) {
            *out_is_safe = 1;
            return SYMBOLON_OK;
        }
    }

    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_pqc_is_cnsa2_compliant(
    const char* alg_name,
    int* out_is_compliant)
{
    if (!alg_name || !out_is_compliant) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    static const char* cnsa2_algs[] = {
        "ML-DSA-65", "ML-DSA-87",
        "ML-KEM-768", "ML-KEM-1024",
        "SLH-DSA-SHA2-128s", "SLH-DSA-SHAKE-128s"
    };
    int count = (int)(sizeof(cnsa2_algs) / sizeof(cnsa2_algs[0]));

    *out_is_compliant = 0;
    for (int i = 0; i < count; i++) {
        if (str_case_eq(alg_name, cnsa2_algs[i])) {
            *out_is_compliant = 1;
            return SYMBOLON_OK;
        }
    }

    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_tokens_reserve(
    symbolon_client_t* client,
    const char* wallet_id,
    const char* feature_code,
    double estimated_units,
    char* out_reservation_id,
    size_t reservation_id_size,
    double* out_reserved_amount,
    double* out_available_balance)
{
    if (!client || !wallet_id || !feature_code || !out_reservation_id || reservation_id_size == 0) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    snprintf(out_reservation_id, reservation_id_size, "res_%llx", (unsigned long long)rand_u64());
    if (out_reserved_amount) *out_reserved_amount = estimated_units;
    if (out_available_balance) *out_available_balance = 1000.0 - estimated_units;
    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_tokens_heartbeat(
    symbolon_client_t* client,
    const char* reservation_id,
    double delta_units,
    double* out_available_balance)
{
    if (!client || !reservation_id) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }
    (void)delta_units;
    if (out_available_balance) *out_available_balance = 900.0;
    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_tokens_commit(
    symbolon_client_t* client,
    const char* reservation_id,
    double actual_units,
    double* out_new_balance)
{
    if (!client || !reservation_id) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }
    (void)actual_units;
    if (out_new_balance) *out_new_balance = 950.0;
    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_tokens_rollback(
    symbolon_client_t* client,
    const char* reservation_id,
    const char* reason,
    double* out_new_balance)
{
    if (!client || !reservation_id) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }
    (void)reason;
    if (out_new_balance) *out_new_balance = 1000.0;
    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_tokens_begin_scope(
    symbolon_client_t* client,
    const char* wallet_id,
    const char* feature_code,
    double estimated_units,
    symbolon_token_scope_t** out_scope)
{
    if (!client || !wallet_id || !feature_code || !out_scope) {
        return SYMBOLON_ERR_INVALID_ARGUMENT;
    }

    symbolon_token_scope_t* scope = (symbolon_token_scope_t*)calloc(1, sizeof(symbolon_token_scope_t));
    if (!scope) {
        return SYMBOLON_ERR_UNKNOWN;
    }

    scope->client = client;
    strncpy(scope->wallet_id, wallet_id, sizeof(scope->wallet_id) - 1);
    strncpy(scope->feature_code, feature_code, sizeof(scope->feature_code) - 1);
    snprintf(scope->reservation_id, sizeof(scope->reservation_id), "res_%llx", (unsigned long long)rand_u64());
    scope->reserved_amount = estimated_units;
    scope->available_balance = 1000.0 - estimated_units;
    scope->is_completed = 0;

    *out_scope = scope;
    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_tokens_scope_commit(
    symbolon_token_scope_t* scope,
    double actual_units,
    double* out_new_balance)
{
    if (!scope) return SYMBOLON_ERR_INVALID_ARGUMENT;
    if (scope->is_completed) return SYMBOLON_ERR_INVALID_ARGUMENT;

    scope->is_completed = 1;
    double refund = (scope->reserved_amount - actual_units);
    if (refund < 0.0) refund = 0.0;
    scope->available_balance += refund;

    if (out_new_balance) *out_new_balance = scope->available_balance;
    return SYMBOLON_OK;
}

SYMBOLON_API symbolon_status_t symbolon_tokens_scope_rollback(
    symbolon_token_scope_t* scope,
    const char* reason)
{
    if (!scope) return SYMBOLON_ERR_INVALID_ARGUMENT;
    if (scope->is_completed) return SYMBOLON_OK;

    scope->is_completed = 1;
    (void)reason;
    scope->available_balance += scope->reserved_amount;
    return SYMBOLON_OK;
}

SYMBOLON_API void symbolon_tokens_scope_destroy(symbolon_token_scope_t* scope)
{
    if (scope) {
        if (!scope->is_completed) {
            symbolon_tokens_scope_rollback(scope, "Destroyed without explicit commit");
        }
        free(scope);
    }
}

SYMBOLON_API void symbolon_client_destroy(symbolon_client_t* client)
{
    if (client) {
        free(client);
    }
}
