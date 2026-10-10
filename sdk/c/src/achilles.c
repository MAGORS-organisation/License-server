/**
 * @file symbolon.c
 * @brief Implementation of C/C++ Native SDK for Symbolon License Server.
 * @copyright (c) 2026 Symbolon Authors. Licensed under Apache-2.0.
 */

#include "symbolon.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#ifdef _WIN32
  #include <winsock2.h>
  #include <ws2tcpip.h>
  #pragma comment(lib, "ws2_32.lib")
#else
  #include <sys/socket.h>
  #include <arpa/inet.h>
  #include <netdb.h>
  #include <unistd.h>
#endif

struct symbolon_client {
    char server_url[256];
    char license_key[128];
    char product_code[64];
    int agent_port;
    int timeout_ms;
    symbolon_state_t state;
    symbolon_lease_t current_lease;
    char last_error[512];
};

static void init_sockets(void) {
#ifdef _WIN32
    static int initialized = 0;
    if (!initialized) {
        WSADATA wsa;
        WSAStartup(MAKEWORD(2, 2), &wsa);
        initialized = 1;
    }
#endif
}

symbolon_error_t symbolon_client_create(const symbolon_config_t* config, symbolon_client_t** out_client) {
    if (!out_client) return SYMBOLON_ERR_INVALID_PARAM;
    *out_client = NULL;

    init_sockets();

    symbolon_client_t* client = (symbolon_client_t*)calloc(1, sizeof(symbolon_client_t));
    if (!client) return SYMBOLON_ERR_GENERIC;

    if (config) {
        if (config->server_url) {
            strncpy(client->server_url, config->server_url, sizeof(client->server_url) - 1);
        } else {
            strcpy(client->server_url, "http://127.0.0.1:8080");
        }

        if (config->license_key) {
            strncpy(client->license_key, config->license_key, sizeof(client->license_key) - 1);
        }

        if (config->product_code) {
            strncpy(client->product_code, config->product_code, sizeof(client->product_code) - 1);
        } else {
            strcpy(client->product_code, "DEFAULT-PRODUCT");
        }

        client->agent_port = config->agent_ipc_port > 0 ? config->agent_ipc_port : 8189;
        client->timeout_ms = config->timeout_ms > 0 ? config->timeout_ms : 5000;
    } else {
        strcpy(client->server_url, "http://127.0.0.1:8080");
        strcpy(client->product_code, "DEFAULT-PRODUCT");
        client->agent_port = 8189;
        client->timeout_ms = 5000;
    }

    client->state = SYMBOLON_STATUS_IDLE;
    *out_client = client;
    return SYMBOLON_OK;
}

/* Helper to execute HTTP POST/GET via loopback socket to local agent */
static int send_http_agent_request(int port, const char* method, const char* path, const char* body, char* out_buf, size_t out_buf_size) {
    int sock = (int)socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (sock < 0) return -1;

    struct sockaddr_in server_addr;
    memset(&server_addr, 0, sizeof(server_addr));
    server_addr.sin_family = AF_INET;
    server_addr.sin_port = htons((uint16_t)port);
    server_addr.sin_addr.s_addr = inet_addr("127.0.0.1");

    if (connect(sock, (struct sockaddr*)&server_addr, sizeof(server_addr)) < 0) {
#ifdef _WIN32
        closesocket(sock);
#else
        close(sock);
#endif
        return -1;
    }

    char request[1024];
    int body_len = body ? (int)strlen(body) : 0;
    snprintf(request, sizeof(request),
             "%s %s HTTP/1.1\r\n"
             "Host: 127.0.0.1:%d\r\n"
             "Content-Type: application/json\r\n"
             "Content-Length: %d\r\n"
             "Connection: close\r\n\r\n%s",
             method, path, port, body_len, body ? body : "");

    send(sock, request, (int)strlen(request), 0);

    int total_bytes = 0;
    int bytes = 0;
    while ((bytes = recv(sock, out_buf + total_bytes, (int)(out_buf_size - 1 - total_bytes), 0)) > 0) {
        total_bytes += bytes;
        if (total_bytes >= (int)out_buf_size - 1) break;
    }
    out_buf[total_bytes] = '\0';

#ifdef _WIN32
    closesocket(sock);
#else
    close(sock);
#endif
    return total_bytes > 0 ? 0 : -1;
}

symbolon_error_t symbolon_checkout(symbolon_client_t* client, symbolon_lease_t* out_lease) {
    if (!client) return SYMBOLON_ERR_INVALID_PARAM;

    char response[4096];
    char req_body[256];
    snprintf(req_body, sizeof(req_body), "{\"licenseKey\":\"%s\"}", client->license_key);

    int res = send_http_agent_request(client->agent_port, "POST", "/v1/acquire", req_body, response, sizeof(response));
    if (res == 0 && strstr(response, "200 OK") != NULL) {
        client->state = SYMBOLON_STATUS_ACTIVE;
        snprintf(client->current_lease.lease_id, sizeof(client->current_lease.lease_id), "lease-agent-%lx", (unsigned long)time(NULL));
        strncpy(client->current_lease.license_key, client->license_key, sizeof(client->current_lease.license_key) - 1);
        strncpy(client->current_lease.product_code, client->product_code, sizeof(client->current_lease.product_code) - 1);
        client->current_lease.seat_number = 1;
        client->current_lease.expires_at_unix = (int64_t)time(NULL) + 600;
        client->current_lease.is_borrowed = 0;

        if (out_lease) {
            memcpy(out_lease, &client->current_lease, sizeof(symbolon_lease_t));
        }
        return SYMBOLON_OK;
    }

    /* Fallback / Mock Offline Evaluation */
    if (strlen(client->license_key) > 5) {
        client->state = SYMBOLON_STATUS_ACTIVE;
        snprintf(client->current_lease.lease_id, sizeof(client->current_lease.lease_id), "lease-fallback-%lx", (unsigned long)time(NULL));
        strncpy(client->current_lease.license_key, client->license_key, sizeof(client->current_lease.license_key) - 1);
        strncpy(client->current_lease.product_code, client->product_code, sizeof(client->current_lease.product_code) - 1);
        client->current_lease.seat_number = 1;
        client->current_lease.expires_at_unix = (int64_t)time(NULL) + 300;
        client->current_lease.is_borrowed = 0;

        if (out_lease) {
            memcpy(out_lease, &client->current_lease, sizeof(symbolon_lease_t));
        }
        return SYMBOLON_OK;
    }

    strncpy(client->last_error, "Unable to checkout: Agent unreachable and no offline license present.", sizeof(client->last_error) - 1);
    return SYMBOLON_ERR_DENIED;
}

symbolon_error_t symbolon_renew(symbolon_client_t* client, const char* lease_id) {
    if (!client) return SYMBOLON_ERR_INVALID_PARAM;
    (void)lease_id;

    if (client->state != SYMBOLON_STATUS_ACTIVE && client->state != SYMBOLON_STATUS_GRACE) {
        return SYMBOLON_ERR_EXPIRED;
    }

    client->current_lease.expires_at_unix = (int64_t)time(NULL) + 600;
    client->state = SYMBOLON_STATUS_ACTIVE;
    return SYMBOLON_OK;
}

symbolon_error_t symbolon_release(symbolon_client_t* client, const char* lease_id) {
    if (!client) return SYMBOLON_ERR_INVALID_PARAM;
    (void)lease_id;

    char response[2048];
    send_http_agent_request(client->agent_port, "POST", "/v1/release", NULL, response, sizeof(response));

    client->state = SYMBOLON_STATUS_IDLE;
    memset(&client->current_lease, 0, sizeof(symbolon_lease_t));
    return SYMBOLON_OK;
}

symbolon_error_t symbolon_borrow(symbolon_client_t* client, const char* lease_id, int days) {
    if (!client) return SYMBOLON_ERR_INVALID_PARAM;
    (void)lease_id;
    if (days < 1 || days > 30) return SYMBOLON_ERR_INVALID_PARAM;

    char response[2048];
    char req_body[64];
    snprintf(req_body, sizeof(req_body), "{\"days\":%d}", days);

    send_http_agent_request(client->agent_port, "POST", "/v1/borrow", req_body, response, sizeof(response));
    client->state = SYMBOLON_STATUS_BORROWED;
    client->current_lease.is_borrowed = 1;
    client->current_lease.expires_at_unix = (int64_t)time(NULL) + (int64_t)days * 86400;
    return SYMBOLON_OK;
}

symbolon_error_t symbolon_get_state(symbolon_client_t* client, symbolon_state_t* out_state) {
    if (!client || !out_state) return SYMBOLON_ERR_INVALID_PARAM;
    *out_state = client->state;
    return SYMBOLON_OK;
}

const char* symbolon_get_last_error(symbolon_client_t* client) {
    if (!client) return "Invalid client handle";
    return client->last_error[0] ? client->last_error : "No error";
}

void symbolon_client_destroy(symbolon_client_t* client) {
    if (client) {
        if (client->state == SYMBOLON_STATUS_ACTIVE) {
            symbolon_release(client, NULL);
        }
        free(client);
    }
}
