using System.Runtime.InteropServices;
using System.Text;

namespace Symbolon.Domain.Enforcement;

public sealed record BpfKernelCompatibility(
    bool IsSupportedOs,
    string KernelRelease,
    bool BpfFsMounted,
    bool CgroupV2Available,
    string RecommendedHook
);

public static class EbpfKernelLoader
{
    public const string DefaultBpfPinPath = "/sys/fs/bpf/symbolon";
    public const string DefaultLicenseMapPath = "/sys/fs/bpf/symbolon/license_map";

    public static BpfKernelCompatibility CheckCompatibility()
    {
        bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        string kernel = RuntimeInformation.OSDescription;

        bool bpfFsMounted = false;
        bool cgroupV2Available = false;

        if (isLinux)
        {
            try
            {
                bpfFsMounted = Directory.Exists("/sys/fs/bpf");
                cgroupV2Available = Directory.Exists("/sys/fs/cgroup/cgroup.controllers") || Directory.Exists("/sys/fs/cgroup");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Access restricted or containerized
            }
        }

        return new BpfKernelCompatibility(
            IsSupportedOs: isLinux,
            KernelRelease: kernel,
            BpfFsMounted: bpfFsMounted,
            CgroupV2Available: cgroupV2Available,
            RecommendedHook: "cgroup/connect4, cgroup/connect6"
        );
    }

    public static string GenerateBpfSourceCode()
    {
        return """
// SPDX-License-Identifier: GPL-2.0 OR BSD-3-Clause
/* Symbolon Enterprise eBPF Socket Connect Enforcement Hook */

#include <linux/bpf.h>
#include <linux/types.h>
#include <bpf/bpf_helpers.h>
#include <bpf/bpf_endian.h>

#define MAX_LICENSE_ID_LEN 64

struct lease_entry {
    __u64 expires_at;
    __u32 seat_no;
    char license_id[MAX_LICENSE_ID_LEN];
};

struct violation_event {
    __u64 timestamp;
    __u32 pid;
    __u32 dest_ip;
    __u16 dest_port;
    char reason[32];
};

/* Pin map: /sys/fs/bpf/symbolon/license_map */
struct {
    __uint(type, BPF_MAP_TYPE_HASH);
    __uint(max_entries, 16384);
    __type(key, __u32); /* pid */
    __type(value, struct lease_entry);
} license_map SEC(".maps");

/* Ring buffer for security violations */
struct {
    __uint(type, BPF_MAP_TYPE_RINGBUF);
    __uint(max_entries, 1 << 24); /* 16MB */
} violation_ringbuf SEC(".maps");

SEC("cgroup/connect4")
int symbolon_sock4_connect(struct bpf_sock_addr *ctx)
{
    __u64 pid_tgid = bpf_get_current_pid_tgid();
    __u32 pid = (__u32)(pid_tgid >> 32);

    /* Allow DNS (port 53) and loopback IPC */
    if (ctx->user_port == bpf_htons(53) || ctx->user_ip4 == bpf_htonl(0x7F000001)) {
        return 1;
    }

    struct lease_entry *lease = bpf_map_lookup_elem(&license_map, &pid);
    if (!lease) {
        /* No active lease in kernel map: block outbound connect */
        struct violation_event *event = bpf_ringbuf_reserve(&violation_ringbuf, sizeof(*event), 0);
        if (event) {
            event->timestamp = bpf_ktime_get_ns();
            event->pid = pid;
            event->dest_ip = ctx->user_ip4;
            event->dest_port = bpf_ntohs(ctx->user_port);
            __builtin_memcpy(event->reason, "NO_ACTIVE_LEASE", 16);
            bpf_ringbuf_submit(event, 0);
        }
        return 0; /* -EPERM */
    }

    __u64 now_sec = bpf_ktime_get_ns() / 1000000000;
    if (now_sec > lease->expires_at) {
        /* Lease expired in kernel */
        return 0; /* -EPERM */
    }

    return 1; /* ALLOW */
}

char _license[] SEC("license") = "Dual BSD/GPL";
""";
    }
}
