// SPDX-License-Identifier: Apache-2.0 OR GPL-2.0-only
/*
 * Symbolon eBPF Kernel Socket Enforcement Program
 * Attaches to cgroup/connect4 and cgroup/connect6 to enforce licensing at the Linux kernel boundary.
 *
 * Checks kernel BPF map 'license_map' for valid active lease tokens.
 * Rejects unauthorized or expired socket connection attempts with -EPERM.
 * Emits violation events to 'events_ringbuf'.
 */

#ifndef __KERNEL__
#define __KERNEL__
#endif

typedef unsigned char __u8;
typedef unsigned short __u16;
typedef unsigned int __u32;
typedef unsigned long long __u64;

#define SEC(NAME) __attribute__((section(NAME), used))

enum bpf_map_type {
    BPF_MAP_TYPE_UNSPEC = 0,
    BPF_MAP_TYPE_HASH = 1,
    BPF_MAP_TYPE_RINGBUF = 27,
};

/* BPF Map Definition for BPF CO-RE / libbpf */
struct bpf_map_def {
    __u32 type;
    __u32 key_size;
    __u32 value_size;
    __u32 max_entries;
    __u32 map_flags;
};

/* License Lease Entry in Kernel BPF Map */
struct license_entry {
    __u64 expires_at_ns;
    __u32 seats;
    __u8  license_id[32];
    __u8  is_valid;
    __u8  reserved[3];
};

/* Security Violation Event forwarded to userspace via RingBuffer */
struct violation_event {
    __u64 cgroup_id;
    __u32 pid;
    __u32 dst_ip;
    __u16 dst_port;
    __u64 timestamp_ns;
    __u8  reason[32];
};

/* Socket address structure supplied by kernel hook */
struct bpf_sock_addr {
    __u32 user_family;
    __u32 user_ip4;
    __u32 user_ip6[4];
    __u32 user_port;
    __u32 family;
    __u32 type;
    __u32 protocol;
    __u32 msg_src_ip4;
    __u32 msg_src_ip6[4];
};

/* BPF Helpers prototypes */
static void *(*bpf_map_lookup_elem)(void *map, const void *key) = (void *) 1;
static __u64 (*bpf_ktime_get_ns)(void) = (void *) 5;
static __u64 (*bpf_get_current_pid_tgid)(void) = (void *) 14;
static __u64 (*bpf_get_current_cgroup_id)(void) = (void *) 80;
static long (*bpf_ringbuf_output)(void *ringbuf, void *data, __u64 size, __u64 flags) = (void *) 130;

/* BPF Map: Active License Registry (Key: cgroup_id or PID) */
SEC(".maps")
struct bpf_map_def license_map = {
    .type = BPF_MAP_TYPE_HASH,
    .key_size = sizeof(__u64),
    .value_size = sizeof(struct license_entry),
    .max_entries = 65536,
    .map_flags = 0,
};

/* BPF Map: Security Violation Events RingBuffer */
SEC(".maps")
struct bpf_map_def events_ringbuf = {
    .type = BPF_MAP_TYPE_RINGBUF,
    .key_size = 0,
    .value_size = 0,
    .max_entries = 1 << 20, /* 1 MB ring buffer */
    .map_flags = 0,
};

static __inline int enforce_license_policy(struct bpf_sock_addr *ctx, __u32 dst_ip, __u16 dst_port)
{
    __u64 cgroup_id = bpf_get_current_cgroup_id();
    __u64 pid_tgid = bpf_get_current_pid_tgid();
    __u32 pid = (__u32)(pid_tgid >> 32);
    __u64 now_ns = bpf_ktime_get_ns();

    /* 1. Lookup license by cgroup_id first */
    struct license_entry *entry = (struct license_entry *)bpf_map_lookup_elem(&license_map, &cgroup_id);

    /* 2. Fallback lookup by PID if cgroup mapping is not defined */
    if (!entry) {
        __u64 pid_key = (__u64)pid;
        entry = (struct license_entry *)bpf_map_lookup_elem(&license_map, &pid_key);
    }

    /* 3. Check lease validity and expiration */
    if (!entry || entry->is_valid == 0 || entry->expires_at_ns < now_ns) {
        /* Violation detected: emit event into RingBuffer */
        struct violation_event evt = {
            .cgroup_id = cgroup_id,
            .pid = pid,
            .dst_ip = dst_ip,
            .dst_port = dst_port,
            .timestamp_ns = now_ns,
        };

        if (!entry) {
            __builtin_memcpy(evt.reason, "no_lease_assigned", 18);
        } else if (entry->expires_at_ns < now_ns) {
            __builtin_memcpy(evt.reason, "lease_expired", 14);
        } else {
            __builtin_memcpy(evt.reason, "lease_revoked", 14);
        }

        bpf_ringbuf_output(&events_ringbuf, &evt, sizeof(evt), 0);

        /* Return 0 to reject connection with -EPERM */
        return 0;
    }

    /* License is valid and unexpired: allow connection */
    return 1;
}

SEC("cgroup/connect4")
int symbolon_sock4_connect(struct bpf_sock_addr *ctx)
{
    if (!ctx)
        return 1;

    __u32 dst_ip = ctx->user_ip4;
    __u16 dst_port = (__u16)(ctx->user_port >> 16);

    /* Bypass localhost loopback (127.0.0.0/8) so local heartbeat/IPC is never blocked */
    if ((dst_ip & 0x000000FF) == 127) {
        return 1;
    }

    return enforce_license_policy(ctx, dst_ip, dst_port);
}

SEC("cgroup/connect6")
int symbolon_sock6_connect(struct bpf_sock_addr *ctx)
{
    if (!ctx)
        return 1;

    /* IPv6 ::1 loopback bypass */
    if (ctx->user_ip6[0] == 0 && ctx->user_ip6[1] == 0 &&
        ctx->user_ip6[2] == 0 && ctx->user_ip6[3] == 0x01000000) {
        return 1;
    }

    __u16 dst_port = (__u16)(ctx->user_port >> 16);
    return enforce_license_policy(ctx, 0, dst_port);
}

char _license[] SEC("license") = "Dual Apache/GPL";
