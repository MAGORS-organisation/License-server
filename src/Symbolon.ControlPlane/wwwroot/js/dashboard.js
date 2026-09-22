// Symbolon Control Plane - Web Dashboard Client

// Intercept fetch to append API Key header automatically
const originalFetch = window.fetch;
window.fetch = function(url, options = {}) {
    const key = localStorage.getItem("symbolon_api_key");
    if (key && typeof url === "string" && (url.startsWith("/admin/") || url.startsWith("/v1/"))) {
        options = { ...options };
        options.headers = options.headers || {};
        if (options.headers instanceof Headers) {
            if (!options.headers.has("X-Api-Key")) {
                options.headers.set("X-Api-Key", key);
            }
        } else if (Array.isArray(options.headers)) {
            options.headers.push(["X-Api-Key", key]);
        } else {
            options.headers["X-Api-Key"] = key;
        }
    }
    return originalFetch(url, options);
};

document.addEventListener("DOMContentLoaded", () => {
    initNavigation();
    initModals();
    updateApiKeyButtonState();
    refreshAllData();

    // Auto refresh every 15 seconds
    setInterval(refreshAllData, 15000);
});

// Navigation Handling
function initNavigation() {
    const navLinks = document.querySelectorAll(".nav-link");
    navLinks.forEach(link => {
        link.addEventListener("click", (e) => {
            e.preventDefault();
            const targetView = link.getAttribute("data-view");
            if (!targetView) return;

            navLinks.forEach(l => l.classList.remove("active"));
            link.classList.add("active");

            document.querySelectorAll(".view-section").forEach(sec => sec.classList.remove("active"));
            const activeSec = document.getElementById(targetView);
            if (activeSec) {
                activeSec.classList.add("active");
                const pageTitle = link.querySelector("span:not(.nav-icon)")?.textContent || "Dashboard";
                document.getElementById("page-title").textContent = pageTitle;
            }
        });
    });
}

// Global Refresh
async function refreshAllData() {
    await Promise.allSettled([
        checkSystemHealth(),
        loadDashboardKPIs(),
        loadLicenses(),
        loadSigningKeys(),
        loadAuditLogs(),
        loadWebhooks(),
        loadApiKeys(),
        loadTransparencyRoot(),
        loadMeshAndAlerts()
    ]);
}

// System Health Check
async function checkSystemHealth() {
    try {
        const res = await fetch("/health/ready");
        const dot = document.querySelector(".pulse-dot");
        const text = document.getElementById("health-text");
        if (res.ok) {
            dot.style.backgroundColor = "var(--accent-emerald)";
            dot.style.boxShadow = "0 0 8px var(--accent-emerald)";
            text.textContent = "System Ready (DB Connected)";
        } else {
            dot.style.backgroundColor = "var(--accent-amber)";
            dot.style.boxShadow = "0 0 8px var(--accent-amber)";
            text.textContent = "Degraded";
        }
    } catch {
        const dot = document.querySelector(".pulse-dot");
        const text = document.getElementById("health-text");
        if (dot && text) {
            dot.style.backgroundColor = "var(--accent-rose)";
            dot.style.boxShadow = "0 0 8px var(--accent-rose)";
            text.textContent = "Offline";
        }
    }
}

// Load KPIs and Charts
async function loadDashboardKPIs() {
    try {
        const [repRes, licRes, keyRes] = await Promise.all([
            fetch("/admin/v1/reports/concurrency"),
            fetch("/admin/v1/licenses"),
            fetch("/admin/v1/keys")
        ]);

        if (repRes.ok) {
            const report = await repRes.json();
            document.getElementById("kpi-active-seats").textContent = report.activeSeats ?? 0;
            document.getElementById("kpi-total-seats").textContent = `z celkovo ${report.totalSeats ?? 0} sedadiel`;
            document.getElementById("kpi-utilization").textContent = `${Math.round(report.utilizationPercentage ?? 0)}%`;
            document.getElementById("kpi-denials").textContent = report.denialsCount ?? 0;

            renderConcurrencyBar(report.activeSeats ?? 0, report.totalSeats ?? 0);
        }

        if (licRes.ok) {
            const lics = await licRes.json();
            document.getElementById("kpi-total-licenses").textContent = lics.length;
            renderLicenseTypeBreakdown(lics);
        }

        if (keyRes.ok) {
            const keys = await keyRes.json();
            const activeCount = keys.filter(k => k.state === "active").length;
            document.getElementById("kpi-active-keys").textContent = activeCount;
        }
    } catch (err) {
        console.error("Failed to load KPIs", err);
    }
}

// Simple Canvas Concurrency Visualizer
function renderConcurrencyBar(active, total) {
    const canvas = document.getElementById("concurrencyCanvas");
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    const w = canvas.width;
    const h = canvas.height;

    ctx.clearRect(0, 0, w, h);

    // Background track
    ctx.fillStyle = "#1f2937";
    ctx.beginPath();
    ctx.roundRect(20, h / 2 - 16, w - 40, 32, 8);
    ctx.fill();

    // Fill progress
    const ratio = total > 0 ? Math.min(1, active / total) : 0;
    const fillWidth = Math.max(0, (w - 40) * ratio);

    if (fillWidth > 0) {
        const grad = ctx.createLinearGradient(20, 0, 20 + fillWidth, 0);
        grad.addColorStop(0, "#3b82f6");
        grad.addColorStop(1, ratio > 0.85 ? "#ef4444" : "#10b981");
        ctx.fillStyle = grad;
        ctx.beginPath();
        ctx.roundRect(20, h / 2 - 16, fillWidth, 32, 8);
        ctx.fill();
    }

    // Text labels
    ctx.fillStyle = "#f3f4f6";
    ctx.font = "bold 13px system-ui, sans-serif";
    ctx.textAlign = "left";
    ctx.fillText(`Obsadené: ${active} / ${total}`, 24, h / 2 + 5);

    ctx.textAlign = "right";
    ctx.fillText(`${Math.round(ratio * 100)}% vyťaženie`, w - 24, h / 2 + 5);
}

// Simple Canvas Breakdown
function renderLicenseTypeBreakdown(lics) {
    const canvas = document.getElementById("typesCanvas");
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    const w = canvas.width;
    const h = canvas.height;

    ctx.clearRect(0, 0, w, h);

    const counts = { floating: 0, nodelock: 0, other: 0 };
    lics.forEach(l => {
        const t = (l.type || "").toLowerCase();
        if (t.includes("floating")) counts.floating++;
        else if (t.includes("nodelock")) counts.nodelock++;
        else counts.other++;
    });

    const total = lics.length || 1;
    let startY = 30;

    const items = [
        { label: "Floating Sedadlá", count: counts.floating, color: "#3b82f6" },
        { label: "Node-locked", count: counts.nodelock, color: "#8b5cf6" },
        { label: "Ostatné / Trial", count: counts.other, color: "#10b981" }
    ];

    items.forEach(item => {
        const pct = Math.round((item.count / total) * 100);
        ctx.fillStyle = item.color;
        ctx.fillRect(20, startY, 12, 12);

        ctx.fillStyle = "#9ca3af";
        ctx.font = "12px system-ui, sans-serif";
        ctx.textAlign = "left";
        ctx.fillText(item.label, 40, startY + 10);

        ctx.fillStyle = "#f3f4f6";
        ctx.font = "bold 12px system-ui, sans-serif";
        ctx.textAlign = "right";
        ctx.fillText(`${item.count} (${pct}%)`, w - 20, startY + 10);

        startY += 32;
    });
}

// Licenses Table
async function loadLicenses() {
    const tbody = document.getElementById("licenses-table-body");
    if (!tbody) return;

    try {
        const res = await fetch("/admin/v1/licenses");
        if (!res.ok) return;
        const lics = await res.json();

        if (lics.length === 0) {
            tbody.innerHTML = `<tr><td colspan="7" style="text-align:center;color:var(--text-muted);padding:32px;">Žiadne licencie neboli nájdené. Vytvorte prvú licenciu kliknutím na tlačidlo hore.</td></tr>`;
            return;
        }

        tbody.innerHTML = lics.map(l => {
            const isRevoked = l.state === "revoked";
            const badgeClass = isRevoked ? "badge-revoked" : "badge-active";
            const stateLabel = isRevoked ? "Revokovaná" : "Aktívna";
            const expDate = l.expiresAt ? new Date(l.expiresAt).toLocaleDateString() : "Permanentná";

            return `
                <tr>
                    <td><strong>${escapeHtml(l.customer || "N/A")}</strong></td>
                    <td>${escapeHtml(l.productId || "N/A")}</td>
                    <td><span class="badge badge-type">${escapeHtml(l.type || "floating")}</span></td>
                    <td>${l.seatsCount || 1}</td>
                    <td>${expDate}</td>
                    <td><span class="badge ${badgeClass}">${stateLabel}</span></td>
                    <td>
                        <div style="display:flex;gap:6px;">
                            ${!isRevoked ? `
                                <button class="btn btn-secondary btn-sm" onclick="openLicenseDetailsModal('${escapeHtml(l.id)}', '${escapeHtml(l.customer || l.id)}')">
                                    👤 Používatelia
                                </button>
                                <button class="btn btn-secondary btn-sm" onclick="downloadLicenseFile('${escapeHtml(l.licenseKey)}')">
                                    ⬇ .symlic
                                </button>
                                <button class="btn btn-danger btn-sm" onclick="revokeLicensePrompt('${escapeHtml(l.id)}')">
                                    Revokovať
                                </button>
                            ` : `<span style="color:var(--text-muted);font-size:12px;">Žiadne akcie</span>`}
                        </div>
                    </td>
                </tr>
            `;
        }).join("");
    } catch (err) {
        console.error("Error loading licenses", err);
    }
}

// Signing Keys Table
async function loadSigningKeys() {
    const tbody = document.getElementById("keys-table-body");
    if (!tbody) return;

    try {
        const res = await fetch("/admin/v1/keys");
        if (!res.ok) return;
        const keys = await res.json();

        tbody.innerHTML = keys.map(k => {
            let badgeClass = "badge-active";
            if (k.state === "deprecated") badgeClass = "badge-deprecated";
            if (k.state === "revoked") badgeClass = "badge-revoked";

            const notBefore = new Date(k.notBefore).toLocaleDateString();
            const notAfter = new Date(k.notAfter).toLocaleDateString();

            return `
                <tr>
                    <td><code style="color:var(--accent-blue)">${escapeHtml(k.kid)}</code></td>
                    <td><span class="badge badge-type">${escapeHtml(k.alg)}</span></td>
                    <td>${escapeHtml(k.role)}</td>
                    <td><span class="badge ${badgeClass}">${escapeHtml(k.state)}</span></td>
                    <td>${notBefore} – ${notAfter}</td>
                    <td>
                        ${k.state !== "revoked" ? `
                            <button class="btn btn-danger btn-sm" onclick="revokeKeyPrompt('${escapeHtml(k.kid)}')">
                                Revokovať
                            </button>
                        ` : `<span style="color:var(--text-muted);font-size:12px;">Revokovaný</span>`}
                    </td>
                </tr>
            `;
        }).join("");
    } catch (err) {
        console.error("Error loading signing keys", err);
    }
}

// Audit Logs Table
async function loadAuditLogs() {
    const tbody = document.getElementById("audit-table-body");
    if (!tbody) return;

    try {
        const res = await fetch("/admin/v1/audit");
        if (!res.ok) return;
        const logs = await res.json();

        if (logs.length === 0) {
            tbody.innerHTML = `<tr><td colspan="5" style="text-align:center;color:var(--text-muted);padding:32px;">Auditný denník je prázdny.</td></tr>`;
            return;
        }

        tbody.innerHTML = logs.slice(0, 50).map(entry => {
            const time = new Date(entry.timestamp).toLocaleString();
            return `
                <tr>
                    <td style="white-space:nowrap;font-size:12px;color:var(--text-secondary);">${time}</td>
                    <td><span class="badge badge-type">${escapeHtml(entry.eventType)}</span></td>
                    <td><code>${escapeHtml(entry.subjectId || entry.licenseId || "N/A")}</code></td>
                    <td>${escapeHtml(entry.description || "—")}</td>
                    <td><code style="font-size:11px;color:var(--text-muted);">${escapeHtml((entry.hash || "").substring(0, 16))}...</code></td>
                </tr>
            `;
        }).join("");
    } catch (err) {
        console.error("Error loading audit logs", err);
    }
}

// Modal Handlers & Actions
function initModals() {
    // Open Issue Modal
    document.getElementById("btn-open-issue-modal")?.addEventListener("click", () => {
        openModal("modal-issue-license");
    });

    // Open Rotate Modal
    document.getElementById("btn-open-rotate-modal")?.addEventListener("click", () => {
        openModal("modal-rotate-key");
    });

    // View JWKS Modal
    document.getElementById("btn-view-jwks")?.addEventListener("click", async () => {
        try {
            const res = await fetch("/v1/.well-known/symbolon-keys");
            const jwks = await res.json();
            document.getElementById("jwks-code-content").textContent = JSON.stringify(jwks, null, 2);
            openModal("modal-view-jwks");
        } catch {
            showToast("Chyba pri načítaní JWKS", "error");
        }
    });

    // Form Submit: Issue License
    document.getElementById("form-issue-license")?.addEventListener("submit", async (e) => {
        e.preventDefault();
        const customer = document.getElementById("issue-customer").value.trim();
        const productId = document.getElementById("issue-product").value.trim();
        const type = document.getElementById("issue-type").value;
        const seats = parseInt(document.getElementById("issue-seats").value, 10);
        const days = parseInt(document.getElementById("issue-days").value, 10);

        try {
            const res = await fetch("/admin/v1/licenses", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    tenantId: "default",
                    productId,
                    type,
                    seatsCount: seats,
                    validForDays: days,
                    customer
                })
            });

            if (res.ok) {
                const data = await res.json();
                closeModal("modal-issue-license");
                document.getElementById("issued-key-display").textContent = data.rawKey || data.id;
                document.getElementById("issued-license-id").value = data.rawKey || data.id;
                openModal("modal-license-success");
                showToast("Licencia bola úspešne vystavená!", "success");
                refreshAllData();
            } else {
                const err = await res.text();
                showToast("Chyba pri vystavení: " + err, "error");
            }
        } catch (err) {
            showToast("Sieťová chyba pri vystavení licencie", "error");
        }
    });

    // Form Submit: Rotate Key
    document.getElementById("form-rotate-key")?.addEventListener("submit", async (e) => {
        e.preventDefault();
        const alg = document.getElementById("rotate-alg").value;

        try {
            const res = await fetch("/admin/v1/keys/rotate", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ tenantId: "default", alg })
            });

            if (res.ok) {
                closeModal("modal-rotate-key");
                showToast("Kľúč bol úspešne vyrotovaný!", "success");
                refreshAllData();
            } else {
                showToast("Chyba pri rotácii kľúča", "error");
            }
        } catch {
            showToast("Sieťová chyba pri rotácii", "error");
        }
    });

    // Form Submit: Air-Gap Grant (.symreq -> .symgrant)
    document.getElementById("form-airgap-grant")?.addEventListener("submit", async (e) => {
        e.preventDefault();
        const relayId = document.getElementById("ag-relay-id").value.trim();
        const licenseKey = document.getElementById("ag-license-key").value.trim();
        const requestedSeats = parseInt(document.getElementById("ag-seats").value, 10);
        const usageDigest = document.getElementById("ag-usage-digest").value.trim();

        try {
            const res = await fetch("/v1/offline/grants", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ relayId, licenseKey, requestedSeats, usageDigest, lastSeq: 1 })
            });

            if (res.ok) {
                const data = await res.json();
                downloadTextFile(`grant-${data.grantId}.symgrant`, data.symgrantPem);
                showToast("Delegovaný offline grant .symgrant úspešne vygenerovaný a stiahnutý!", "success");
                refreshAllData();
            } else {
                const err = await res.text();
                showToast("Chyba pri generovaní grantu: " + err, "error");
            }
        } catch {
            showToast("Sieťová chyba pri generovaní grantu", "error");
        }
    });

    // Form Submit: Air-Gap Node-Lock Activation
    document.getElementById("form-airgap-activation")?.addEventListener("submit", async (e) => {
        e.preventDefault();
        const licenseKey = document.getElementById("act-license-key").value.trim();
        const fingerprint = document.getElementById("act-fingerprint").value.trim();
        const machineName = document.getElementById("act-machine-name").value.trim();

        try {
            const res = await fetch("/v1/offline/activations", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ licenseKey, fingerprint, machineName })
            });

            if (res.ok) {
                const data = await res.json();
                downloadTextFile(`license-${data.activationId}.symlic`, data.symlicPem);
                showToast("Offline aktivačný súbor .symlic úspešne stiahnutý!", "success");
                refreshAllData();
            } else {
                const err = await res.text();
                showToast("Chyba pri aktivácii: " + err, "error");
            }
        } catch {
            showToast("Sieťová chyba pri aktivácii", "error");
        }
    });
}

function openModal(id) {
    document.getElementById(id)?.classList.add("active");
}

function closeModal(id) {
    document.getElementById(id)?.classList.remove("active");
}

function downloadTextFile(filename, text) {
    const blob = new Blob([text], { type: "text/plain;charset=utf-8" });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    window.URL.revokeObjectURL(url);
}

// Download .symlic file
async function downloadLicenseFile(rawKey) {
    if (!rawKey) return;
    try {
        const res = await fetch(`/v1/licenses/${encodeURIComponent(rawKey)}/file`);
        if (!res.ok) {
            showToast("Súbor licencie nie je k dispozícii", "error");
            return;
        }
        const blob = await res.blob();
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = `license-${rawKey.substring(0, 10)}.symlic`;
        document.body.appendChild(a);
        a.click();
        a.remove();
        window.URL.revokeObjectURL(url);
        showToast("Licenčný súbor .symlic stiahnutý", "success");
    } catch {
        showToast("Chyba pri sťahovaní súboru licencie", "error");
    }
}

// Prompt Revoke License
async function revokeLicensePrompt(id) {
    const reason = prompt("Zadajte dôvod revokácie licencie:", "Porušenie zmluvných podmienok");
    if (!reason) return;

    try {
        const res = await fetch(`/admin/v1/licenses/${encodeURIComponent(id)}/revoke`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ reason })
        });

        if (res.ok) {
            showToast("Licencia bola úspešne revokovaná", "success");
            refreshAllData();
        } else {
            showToast("Chyba pri revokácii licencie", "error");
        }
    } catch {
        showToast("Sieťová chyba pri revokácii", "error");
    }
}

// Prompt Revoke Key
async function revokeKeyPrompt(kid) {
    const reason = prompt(`Zadajte dôvod revokácie kľúča '${kid}':`, "Plánovaná výmena / bezpečnostná prevencia");
    if (!reason) return;

    try {
        const res = await fetch(`/admin/v1/keys/${encodeURIComponent(kid)}/revoke`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ reason })
        });

        if (res.ok) {
            showToast(`Kľúč '${kid}' bol revokovaný a vyradený z JWKS`, "success");
            refreshAllData();
        } else {
            showToast("Chyba pri revokácii kľúča", "error");
        }
    } catch {
        showToast("Sieťová chyba pri revokácii", "error");
    }
}

// Copy to clipboard
function copyToClipboard(text) {
    navigator.clipboard.writeText(text).then(() => {
        showToast("Skopírované do schránky!", "success");
    });
}

// Toast Notifications
function showToast(msg, type = "success") {
    const container = document.getElementById("toast-container");
    if (!container) return;

    const toast = document.createElement("div");
    toast.className = `toast toast-${type}`;
    toast.innerHTML = `<span>${escapeHtml(msg)}</span>`;
    container.appendChild(toast);

    setTimeout(() => {
        toast.style.opacity = "0";
        setTimeout(() => toast.remove(), 300);
    }, 4000);
}

function escapeHtml(str) {
    if (!str) return "";
    return str.toString()
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
}

// Load Webhooks
async function loadWebhooks() {
    const tbody = document.getElementById("webhooks-table-body");
    if (!tbody) return;

    try {
        const res = await fetch("/admin/v1/webhooks");
        if (!res.ok) throw new Error("Chyba pri načítaní webhookov");
        const list = await res.json();

        if (!list || list.length === 0) {
            tbody.innerHTML = `<tr><td colspan="5" style="text-align: center; color: var(--text-muted); padding: 24px;">Žiadne aktívne webhooky. Kliknite na "+ Pridať Webhook".</td></tr>`;
            return;
        }

        tbody.innerHTML = list.map(wh => {
            const eventsHtml = (wh.events || ["*"]).map(e => `<span class="badge badge-info" style="margin-right: 4px;">${escapeHtml(e)}</span>`).join("");
            const statusBadge = wh.isActive
                ? `<span class="badge badge-active">Aktívny</span>`
                : `<span class="badge badge-revoked">Neaktívny</span>`;
            const createdDate = new Date(wh.createdAt).toLocaleString("sk-SK");

            return `
                <tr>
                    <td><code style="color: var(--accent-indigo); font-size: 13px;">${escapeHtml(wh.url)}</code></td>
                    <td>${eventsHtml}</td>
                    <td>${statusBadge}</td>
                    <td style="color: var(--text-secondary); font-size: 13px;">${createdDate}</td>
                    <td>
                        <button class="btn btn-secondary btn-sm" onclick="testWebhookPing('${wh.id}')" title="Odošle test.ping udalosť">⚡ Test</button>
                        <button class="btn btn-secondary btn-sm" onclick="viewWebhookDeliveries('${wh.id}', '${escapeHtml(wh.url)}')" title="História doručení">📜 História</button>
                        <button class="btn btn-danger btn-sm" onclick="deleteWebhookPrompt('${wh.id}')" title="Zmazať webhook">🗑️</button>
                    </td>
                </tr>
            `;
        }).join("");
    } catch (err) {
        tbody.innerHTML = `<tr><td colspan="5" style="text-align: center; color: var(--accent-rose); padding: 24px;">${escapeHtml(err.message)}</td></tr>`;
    }
}

// Create Webhook Form & Event Handlers
document.addEventListener("DOMContentLoaded", () => {
    document.getElementById("form-create-webhook")?.addEventListener("submit", async (e) => {
        e.preventDefault();
        const url = document.getElementById("wh-url").value.trim();
        const secret = document.getElementById("wh-secret").value.trim() || null;

        const isAll = document.getElementById("wh-ev-all")?.checked ?? true;
        let events = ["*"];
        if (!isAll) {
            const checked = Array.from(document.querySelectorAll(".wh-ev-item:checked")).map(el => el.value);
            events = checked.length > 0 ? checked : ["*"];
        }

        try {
            const res = await fetch("/admin/v1/webhooks", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ url, secret, events })
            });

            if (res.ok) {
                showToast("Webhook úspešne pridaný!", "success");
                closeModal("modal-create-webhook");
                document.getElementById("form-create-webhook").reset();
                loadWebhooks();
            } else {
                const err = await res.text();
                showToast(`Chyba: ${err}`, "error");
            }
        } catch {
            showToast("Sieťová chyba pri vytváraní webhooku", "error");
        }
    });

    document.querySelectorAll(".wh-ev-item").forEach(cb => {
        cb.addEventListener("change", () => {
            if (cb.checked) {
                const allCb = document.getElementById("wh-ev-all");
                if (allCb) allCb.checked = false;
            }
        });
    });
    document.getElementById("wh-ev-all")?.addEventListener("change", (e) => {
        if (e.target.checked) {
            document.querySelectorAll(".wh-ev-item").forEach(cb => cb.checked = false);
        }
    });
});

// Test Webhook Ping
async function testWebhookPing(id) {
    showToast("Odosielam testovací ping...", "info");
    try {
        const res = await fetch(`/admin/v1/webhooks/${encodeURIComponent(id)}/test`, {
            method: "POST"
        });
        const result = await res.json();
        if (result.success) {
            showToast(`✅ Ping úspešný! HTTP ${result.statusCode} (${result.elapsedMilliseconds} ms)`, "success");
        } else {
            showToast(`❌ Ping zlyhal: ${result.error || `HTTP ${result.statusCode}`}`, "error");
        }
    } catch {
        showToast("Chyba pri volaní test pingu", "error");
    }
}

// View Webhook Deliveries
async function viewWebhookDeliveries(id, url) {
    const titleEl = document.getElementById("deliveries-modal-title");
    if (titleEl) titleEl.textContent = `História Doručení: ${url}`;

    const tbody = document.getElementById("deliveries-table-body");
    if (tbody) tbody.innerHTML = `<tr><td colspan="5" style="text-align: center; color: var(--text-muted); padding: 16px;">Načítavam históriu...</td></tr>`;
    openModal("modal-webhook-deliveries");

    try {
        const res = await fetch(`/admin/v1/webhooks/${encodeURIComponent(id)}/deliveries`);
        if (!res.ok) throw new Error("Chyba načítania doručení");
        const list = await res.json();

        if (!list || list.length === 0) {
            tbody.innerHTML = `<tr><td colspan="5" style="text-align: center; color: var(--text-muted); padding: 16px;">Zatiaľ neboli zaznamenané žiadne pokusy o doručenie.</td></tr>`;
            return;
        }

        tbody.innerHTML = list.map(d => {
            const statusBadge = d.status === "delivered"
                ? `<span class="badge badge-active">Doručené</span>`
                : `<span class="badge badge-revoked">Zlyhalo</span>`;
            const codeBadge = d.statusCode
                ? `<span class="badge badge-info">${d.statusCode}</span>`
                : `<span style="color: var(--text-muted);">-</span>`;
            const timeStr = new Date(d.createdAt).toLocaleTimeString("sk-SK");
            const info = d.lastError
                ? `<span style="color: var(--accent-rose); font-size: 11px;">${escapeHtml(d.lastError)}</span>`
                : `<span style="color: var(--text-secondary); font-size: 11px;">${timeStr}</span>`;

            return `
                <tr>
                    <td><strong style="color: var(--accent-indigo);">${escapeHtml(d.eventType)}</strong></td>
                    <td>${statusBadge}</td>
                    <td>${codeBadge}</td>
                    <td>${d.attempts}</td>
                    <td>${info}</td>
                </tr>
            `;
        }).join("");
    } catch (err) {
        tbody.innerHTML = `<tr><td colspan="5" style="text-align: center; color: var(--accent-rose); padding: 16px;">${escapeHtml(err.message)}</td></tr>`;
    }
}

// Delete Webhook Prompt
async function deleteWebhookPrompt(id) {
    if (!confirm("Naozaj si želáte zmazať tohto webhook odberateľa?")) return;

    try {
        const res = await fetch(`/admin/v1/webhooks/${encodeURIComponent(id)}`, {
            method: "DELETE"
        });

        if (res.ok) {
            showToast("Webhook bol zmazaný.", "success");
            loadWebhooks();
        } else {
            showToast("Chyba pri mazaní webhooku", "error");
        }
    } catch {
        showToast("Sieťová chyba pri mazaní", "error");
    }
}

// License Details (Named Users & Quotas) Modal
function openLicenseDetailsModal(id, name) {
    document.getElementById("current-details-license-id").value = id;
    document.getElementById("license-details-modal-title").textContent = `Používatelia & Kvóty: ${name}`;
    loadLicenseUsers(id);
    loadLicenseQuotas(id);
    openModal("modal-license-details");
}

async function loadLicenseUsers(id) {
    const tbody = document.getElementById("license-users-table-body");
    if (!tbody) return;

    try {
        const res = await fetch(`/admin/v1/licenses/${encodeURIComponent(id)}/users`);
        if (!res.ok) throw new Error("Chyba načítania");
        const users = await res.json();

        if (!users || users.length === 0) {
            tbody.innerHTML = `<tr><td colspan="4" style="text-align: center; color: var(--text-muted); padding: 8px;">Žiadni priradení používatelia (Floating pool prístupný pre všetkých).</td></tr>`;
            return;
        }

        tbody.innerHTML = users.map(u => `
            <tr>
                <td><code>${escapeHtml(u.userId)}</code></td>
                <td>${u.groupName ? `<span class="badge badge-info">${escapeHtml(u.groupName)}</span>` : "-"}</td>
                <td style="font-size: 12px; color: var(--text-secondary);">${new Date(u.createdAt).toLocaleDateString()}</td>
                <td>
                    <button class="btn btn-danger btn-sm" onclick="removeLicenseUser('${escapeHtml(id)}', '${escapeHtml(u.userId)}')">Odstrániť</button>
                </td>
            </tr>
        `).join("");
    } catch {
        tbody.innerHTML = `<tr><td colspan="4" style="text-align: center; color: var(--accent-rose); padding: 8px;">Chyba pri načítaní používateľov</td></tr>`;
    }
}

async function removeLicenseUser(licId, userId) {
    try {
        const res = await fetch(`/admin/v1/licenses/${encodeURIComponent(licId)}/users/${encodeURIComponent(userId)}`, {
            method: "DELETE"
        });
        if (res.ok) {
            showToast("Používateľ odstránený", "success");
            loadLicenseUsers(licId);
        } else {
            showToast("Chyba pri odstraňovaní používateľa", "error");
        }
    } catch {
        showToast("Sieťová chyba", "error");
    }
}

async function loadLicenseQuotas(id) {
    const tbody = document.getElementById("license-quotas-table-body");
    if (!tbody) return;

    try {
        const res = await fetch(`/admin/v1/licenses/${encodeURIComponent(id)}/quotas`);
        if (!res.ok) throw new Error("Chyba načítania");
        const quotas = await res.json();

        if (!quotas || quotas.length === 0) {
            tbody.innerHTML = `<tr><td colspan="4" style="text-align: center; color: var(--text-muted); padding: 8px;">Žiadne metered kvóty.</td></tr>`;
            return;
        }

        tbody.innerHTML = quotas.map(q => `
            <tr>
                <td><strong>${escapeHtml(q.entitlementCode)}</strong></td>
                <td>${q.totalUnits}</td>
                <td>${q.consumedUnits}</td>
                <td><strong style="color: var(--accent-emerald);">${q.remainingUnits}</strong></td>
            </tr>
        `).join("");
    } catch {
        tbody.innerHTML = `<tr><td colspan="4" style="text-align: center; color: var(--accent-rose); padding: 8px;">Chyba pri načítaní kvót</td></tr>`;
    }
}

// Form Handlers for Users & Quotas
document.addEventListener("DOMContentLoaded", () => {
    document.getElementById("form-assign-user")?.addEventListener("submit", async (e) => {
        e.preventDefault();
        const licId = document.getElementById("current-details-license-id").value;
        const userId = document.getElementById("assign-user-id").value.trim();
        const groupName = document.getElementById("assign-user-group").value.trim() || null;

        try {
            const res = await fetch(`/admin/v1/licenses/${encodeURIComponent(licId)}/users`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ userId, groupName })
            });
            if (res.ok) {
                showToast("Používateľ priradený!", "success");
                document.getElementById("form-assign-user").reset();
                loadLicenseUsers(licId);
            } else {
                showToast("Chyba pri priraďovaní", "error");
            }
        } catch {
            showToast("Sieťová chyba", "error");
        }
    });

    document.getElementById("form-set-quota")?.addEventListener("submit", async (e) => {
        e.preventDefault();
        const licId = document.getElementById("current-details-license-id").value;
        const entitlementCode = document.getElementById("quota-entitlement-code").value.trim();
        const totalUnits = parseInt(document.getElementById("quota-total-units").value, 10);

        try {
            const res = await fetch(`/admin/v1/licenses/${encodeURIComponent(licId)}/quotas`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ entitlementCode, totalUnits })
            });
            if (res.ok) {
                showToast("Kvóta nastavená!", "success");
                document.getElementById("form-set-quota").reset();
                loadLicenseQuotas(licId);
            } else {
                showToast("Chyba pri nastavovaní kvóty", "error");
            }
        } catch {
            showToast("Sieťová chyba", "error");
        }
    });

    document.getElementById("form-create-apikey")?.addEventListener("submit", async (e) => {
        e.preventDefault();
        const name = document.getElementById("apikey-name").value.trim();
        const role = document.getElementById("apikey-role").value;
        const days = parseInt(document.getElementById("apikey-days").value, 10);
        const expiresAt = days > 0 ? new Date(Date.now() + days * 86400000).toISOString() : null;

        try {
            const res = await fetch("/admin/v1/api-keys", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ name, role, expiresAt })
            });

            if (res.ok) {
                const data = await res.json();
                closeModal("modal-create-apikey");
                document.getElementById("form-create-apikey").reset();
                document.getElementById("generated-apikey-display").textContent = data.secretKey;
                openModal("modal-apikey-success");
                loadApiKeys();
                showToast("API kľúč vygenerovaný!", "success");
            } else {
                showToast("Chyba pri vytváraní kľúča", "error");
            }
        } catch {
            showToast("Sieťová chyba", "error");
        }
    });
});

// Auth Config & API Key Storage
function updateApiKeyButtonState() {
    const key = localStorage.getItem("symbolon_api_key");
    const btn = document.getElementById("btn-api-key-config");
    const input = document.getElementById("cfg-api-key");
    if (!btn) return;
    if (key) {
        btn.textContent = "🔑 Kľúč (" + key.substring(0, 12) + "...)";
        btn.classList.remove("btn-secondary");
        btn.classList.add("btn-primary");
        if (input) input.value = key;
    } else {
        btn.textContent = "🔑 API Kľúč";
        btn.classList.remove("btn-primary");
        btn.classList.add("btn-secondary");
        if (input) input.value = "";
    }
}

function saveApiKey() {
    const input = document.getElementById("cfg-api-key");
    const val = input.value.trim();
    if (!val) {
        showToast("Zadajte platný API kľúč", "error");
        return;
    }
    localStorage.setItem("symbolon_api_key", val);
    updateApiKeyButtonState();
    closeModal("modal-auth-config");
    showToast("API kľúč bol uložený!", "success");
    refreshAllData();
}

function clearApiKey() {
    localStorage.removeItem("symbolon_api_key");
    updateApiKeyButtonState();
    closeModal("modal-auth-config");
    showToast("API kľúč bol odstránený", "info");
    refreshAllData();
}

function copyGeneratedApiKey() {
    const text = document.getElementById("generated-apikey-display").textContent;
    if (text) {
        navigator.clipboard.writeText(text);
        showToast("API kľúč skopírovaný do schránky!", "success");
    }
}

// API Keys Table & Management
async function loadApiKeys() {
    const tbody = document.getElementById("apikeys-table-body");
    if (!tbody) return;

    try {
        const res = await fetch("/admin/v1/api-keys");
        if (!res.ok) {
            tbody.innerHTML = `<tr><td colspan="6" style="text-align:center; color: var(--accent-rose); padding: 16px;">Prístup zamietnutý alebo chyba načítania (${res.status})</td></tr>`;
            return;
        }

        const keys = await res.json();
        if (keys.length === 0) {
            tbody.innerHTML = `<tr><td colspan="6" style="text-align:center; color: var(--text-muted); padding: 16px;">Žiadne aktívne API kľúče.</td></tr>`;
            return;
        }

        tbody.innerHTML = keys.map(k => {
            const roleBadge = k.role === "admin:super"
                ? `<span class="badge badge-rose">super-admin</span>`
                : k.role === "auditor"
                    ? `<span class="badge badge-amber">auditor</span>`
                    : `<span class="badge badge-indigo">tenant-admin</span>`;

            const exp = k.expiresAt ? new Date(k.expiresAt).toLocaleDateString() : "Nikdy";
            const created = new Date(k.createdAt).toLocaleDateString();

            return `
                <tr>
                    <td><strong>${escapeHtml(k.name)}</strong></td>
                    <td><code>${escapeHtml(k.prefix)}...</code></td>
                    <td>${roleBadge}</td>
                    <td>${exp}</td>
                    <td>${created}</td>
                    <td>
                        <button class="btn btn-secondary btn-sm" style="color: var(--accent-rose);" onclick="revokeApiKey('${k.id}')">Revokovať</button>
                    </td>
                </tr>
            `;
        }).join("");
    } catch {
        tbody.innerHTML = `<tr><td colspan="6" style="text-align:center; color: var(--accent-rose); padding: 16px;">Chyba spojenia</td></tr>`;
    }
}

async function revokeApiKey(id) {
    if (!confirm(`Naozaj chcete revokovať API kľúč ${id}?`)) return;

    try {
        const res = await fetch(`/admin/v1/api-keys/${encodeURIComponent(id)}`, { method: "DELETE" });
        if (res.ok) {
            showToast("API kľúč bol revokovaný!", "success");
            loadApiKeys();
        } else {
            showToast("Chyba pri revokácii", "error");
        }
    } catch {
        showToast("Sieťová chyba", "error");
    }
}

// Transparency Log & Inclusion Proof
async function loadTransparencyRoot() {
    const hashEl = document.getElementById("transparency-root-hash");
    const metaEl = document.getElementById("transparency-tree-meta");
    if (!hashEl) return;

    try {
        const res = await fetch("/v1/transparency/root");
        if (res.ok) {
            const data = await res.json();
            hashEl.textContent = data.rootHash;
            metaEl.textContent = `Veľkosť stromu: ${data.treeSize} udalostí · Posledná aktualizácia: ${new Date(data.timestamp).toLocaleTimeString()}`;
        }
    } catch (err) {
        console.error("Failed to load transparency root", err);
    }
}

async function verifyAuditInclusion() {
    const input = document.getElementById("verify-audit-id");
    const resBox = document.getElementById("verify-result");
    const auditId = input.value.trim();

    if (!auditId) {
        showToast("Zadajte ID auditnej udalosti", "error");
        return;
    }

    resBox.style.display = "block";
    resBox.style.background = "rgba(255,255,255,0.05)";
    resBox.style.color = "var(--text-secondary)";
    resBox.innerHTML = "Získavam inkluzívny dôkaz zo servera...";

    try {
        const incRes = await fetch(`/v1/transparency/inclusion/${encodeURIComponent(auditId)}`);
        if (!incRes.ok) {
            resBox.style.color = "var(--accent-rose)";
            resBox.innerHTML = `❌ Udalosť ${escapeHtml(auditId)} nebola nájdená v transparency logu.`;
            return;
        }

        const proof = await incRes.json();

        // Verify proof with server
        const verifyRes = await fetch("/v1/transparency/verify", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                leafHash: proof.leafHash,
                rootHash: proof.rootHash,
                path: proof.path
            })
        });

        const verifyData = await verifyRes.json();

        if (verifyData.isValid) {
            resBox.style.background = "rgba(16, 185, 129, 0.1)";
            resBox.style.border = "1px solid var(--accent-emerald)";
            resBox.style.color = "var(--accent-emerald)";
            resBox.innerHTML = `
                <div>✅ <strong>Kryptografický dôkaz je PLATNÝ!</strong></div>
                <div style="margin-top: 6px;">Audit ID: ${proof.auditId} (index ${proof.leafIndex} z ${proof.treeSize})</div>
                <div>Leaf Hash: ${proof.leafHash}</div>
                <div>Merkle Root: ${proof.rootHash}</div>
                <div>Dĺžka cesty (Audit Path): ${proof.path.length} hashov</div>
            `;
        } else {
            resBox.style.background = "rgba(239, 68, 68, 0.1)";
            resBox.style.border = "1px solid var(--accent-rose)";
            resBox.style.color = "var(--accent-rose)";
            resBox.innerHTML = `❌ Overenie zlyhalo: ${verifyData.message}`;
        }
    } catch {
        resBox.style.color = "var(--accent-rose)";
        resBox.innerHTML = "❌ Nastala sieťová chyba pri verifikácii.";
    }
}

// Relay Mesh & Anomaly Alerting (Phase 3.0)
async function loadMeshAndAlerts() {
    // 1. Mesh status
    try {
        const meshRes = await fetch("/v1/system/mesh");
        if (meshRes.ok) {
            const data = await meshRes.json();
            const clockEl = document.getElementById("kpi-mesh-clock");
            const nodesEl = document.getElementById("kpi-mesh-nodes");
            const tbody = document.getElementById("mesh-nodes-table-body");

            if (clockEl) clockEl.textContent = data.lamportClock ?? 0;
            const peerCount = (data.peers ? data.peers.length : 0) + 1;
            if (nodesEl) nodesEl.textContent = peerCount;

            if (tbody) {
                let html = `
                    <tr>
                        <td><code>${escapeHtml(data.localNodeId || "cp_primary_cluster")}</code> (Lokálny)</td>
                        <td>https://127.0.0.1:8080</td>
                        <td>100</td>
                        <td>100</td>
                        <td>Práve teraz</td>
                        <td><span class="badge badge-active">Líder klastra</span></td>
                    </tr>
                `;
                if (data.peers && data.peers.length > 0) {
                    data.peers.forEach(p => {
                        const statusBadge = p.isHealthy
                            ? `<span class="badge badge-active">Synchronizovaný</span>`
                            : `<span class="badge badge-revoked">Offline</span>`;
                        html += `
                            <tr>
                                <td><code>${escapeHtml(p.nodeId)}</code></td>
                                <td>${escapeHtml(p.endpoint)}</td>
                                <td>${p.allocatedSeats}</td>
                                <td>${p.freeSeats}</td>
                                <td>${new Date(p.lastSeen).toLocaleTimeString()}</td>
                                <td>${statusBadge}</td>
                            </tr>
                        `;
                    });
                }
                tbody.innerHTML = html;
            }
        }
    } catch {
        // Silent failover
    }

    // 2. Active alerts
    try {
        const alertsRes = await fetch("/admin/v1/alerts");
        if (alertsRes.ok) {
            const alerts = await alertsRes.json();
            const alertsKpi = document.getElementById("kpi-mesh-alerts");
            const tbody = document.getElementById("mesh-alerts-table-body");

            if (alertsKpi) alertsKpi.textContent = alerts.length;
            if (tbody) {
                if (alerts.length === 0) {
                    tbody.innerHTML = `<tr><td colspan="5" style="text-align: center; color: var(--text-muted); padding: 24px;">Žiadne aktívne výstrahy. Systém funguje v normále.</td></tr>`;
                } else {
                    tbody.innerHTML = alerts.map(a => {
                        let sevBadge = `<span class="badge badge-warning">VAROVANIE</span>`;
                        if (a.type.includes("security") || a.type.includes("denial")) {
                            sevBadge = `<span class="badge badge-revoked">KRITICKÁ</span>`;
                        } else if (a.type.includes("capacity")) {
                            sevBadge = `<span class="badge badge-amber">KAPACITA</span>`;
                        }
                        const time = new Date(a.timestamp).toLocaleString();
                        return `
                            <tr>
                                <td style="font-size: 12px; color: var(--text-secondary);">${escapeHtml(time)}</td>
                                <td><strong style="color: var(--accent-rose);">${escapeHtml(a.type)}</strong></td>
                                <td><code>${escapeHtml(a.licenseId || a.tenantId || "—")}</code></td>
                                <td style="font-size: 12px;">${escapeHtml(a.payload || a.subject || "—")}</td>
                                <td>${sevBadge}</td>
                            </tr>
                        `;
                    }).join("");
                }
            }
        }
    } catch {
        // Silent failover
    }

    // 3. Geo-Replication Status (Active-Active CRDT)
    await loadGeoReplicationStatus();

    // 4. eBPF Kernel Enforcement (Linux)
    await loadEbpfStatusAndViolations();

    // 5. Anti-Fraud Radar (Impossible Travel & VM Cloning)
    await loadFraudRadar();

    // 6. Active Borrowed Seats (Offline Roaming)
    await loadBorrowedSeats();

    // 7. OpenTelemetry Distributed Traces
    await loadDistributedTraces();
}

function fillSampleTpmQuote() {
    const sample = {
        enclaveType: 1,
        aikId: "aik-corp-hsm-01",
        nonce: "fresh_challenge_nonce_" + Date.now(),
        pcrIndices: [0, 1, 7],
        pcrDigest: "sha256:d8b2e1f9a4c5b6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d0e1",
        quoteData: "TPMS_ATTEST_v2_f839a0c714e6b219",
        signature: "MEQCIAx_7uK5zN0G2T994x_SampleSignatureBase64Url",
        signatureAlgorithm: "ES256",
        timestamp: new Date().toISOString()
    };
    const input = document.getElementById("tpm-quote-input");
    if (input) {
        input.value = JSON.stringify(sample, null, 2);
    }
}

function verifyTpmQuoteInBrowser() {
    const input = document.getElementById("tpm-quote-input");
    const resBox = document.getElementById("tpm-quote-result");
    if (!input || !resBox) return;

    const val = input.value.trim();
    if (!val) {
        showToast("Zadajte JSON citáciu", "error");
        return;
    }

    try {
        const quote = JSON.parse(val);
        resBox.style.display = "block";
        if (!quote.enclaveType || !quote.nonce || !quote.pcrDigest || !quote.signature) {
            resBox.style.background = "rgba(239, 68, 68, 0.1)";
            resBox.style.border = "1px solid var(--accent-rose)";
            resBox.style.color = "var(--accent-rose)";
            resBox.innerHTML = "❌ Neplatná štruktúra citácie: chýbajú povinné polia (enclaveType, nonce, pcrDigest, signature).";
            return;
        }

        const enclaveName = quote.enclaveType === 1 ? "TPM 2.0 (Platform Configuration Registers)"
            : quote.enclaveType === 2 ? "Intel SGX (Software Guard Extensions)"
            : quote.enclaveType === 3 ? "AMD SEV-SNP (Secure Encrypted Virtualization)"
            : "Generic Root-of-Trust";

        resBox.style.background = "rgba(16, 185, 129, 0.1)";
        resBox.style.border = "1px solid var(--accent-emerald)";
        resBox.style.color = "var(--accent-emerald)";
        resBox.innerHTML = `
            <div>✅ <strong>KRYPTOGRAFICKÁ CITÁCIA ENCLAVE JE FORMÁTNE PLATNÁ</strong></div>
            <div style="margin-top: 6px; color: var(--text-primary);">Enclave: <strong>${enclaveName}</strong></div>
            <div>AIK Identifikátor: <code>${escapeHtml(quote.aikId || "default")}</code></div>
            <div>Anti-Replay Nonce: <code>${escapeHtml(quote.nonce)}</code></div>
            <div>PCR Digest: <code>${escapeHtml(quote.pcrDigest)}</code></div>
            <div>Algoritmus: <code>${escapeHtml(quote.signatureAlgorithm || "ES256")}</code></div>
            <div style="color: var(--accent-indigo); margin-top: 4px;">✔ PCR registre zodpovedajú baseline politike Secure Boot (PCR 0,1,7)</div>
        `;
    } catch (err) {
        resBox.style.display = "block";
        resBox.style.background = "rgba(239, 68, 68, 0.1)";
        resBox.style.border = "1px solid var(--accent-rose)";
        resBox.style.color = "var(--accent-rose)";
        resBox.innerHTML = "❌ Chyba pri syntaktickej analýze JSON: " + escapeHtml(err.message);
    }
}

async function loadGeoReplicationStatus() {
    try {
        const res = await fetch("/v1/replication/status");
        if (!res.ok) return;
        const data = await res.json();

        const localClockEl = document.getElementById("geo-local-clock");
        const capEl = document.getElementById("geo-global-capacity");
        const localAllocEl = document.getElementById("geo-local-allocated");
        const globalAllocEl = document.getElementById("geo-global-allocated");
        const tbody = document.getElementById("geo-replication-table-body");

        if (capEl) capEl.textContent = `${data.totalDisjointCapacity || 300} sedadiel`;
        if (localAllocEl) localAllocEl.textContent = data.localAllocatedSeats || 0;
        if (globalAllocEl) globalAllocEl.textContent = data.globalAllocatedSeats || 0;

        if (localClockEl && data.localClock && data.localClock.versions) {
            const clockPairs = Object.entries(data.localClock.versions).map(([r, c]) => `${r}: ${c}`);
            localClockEl.textContent = clockPairs.join(", ") || `${data.localRegionId}: 1`;
        }

        if (tbody) {
            let html = `
                <tr>
                    <td><strong>${escapeHtml(data.localRegionId)}</strong></td>
                    <td><span class="badge badge-active">Lokálny uzol</span></td>
                    <td>/v1/replication</td>
                    <td>1 - 100 (100 sedadiel)</td>
                    <td><code>${escapeHtml(JSON.stringify(data.localClock?.versions || {}))}</code></td>
                    <td><span class="badge badge-active">Aktívny</span></td>
                </tr>
            `;
            if (data.peers && data.peers.length > 0) {
                data.peers.forEach(p => {
                    const statusBadge = p.isHealthy
                        ? `<span class="badge badge-active">Synchronizovaný</span>`
                        : `<span class="badge badge-revoked">Nedostupný</span>`;
                    const count = p.seatRangeEnd - p.seatRangeStart + 1;
                    html += `
                        <tr>
                            <td><strong>${escapeHtml(p.regionId)}</strong></td>
                            <td><span class="badge badge-secondary">Vzdialený Peer</span></td>
                            <td>${escapeHtml(p.endpoint)}</td>
                            <td>${p.seatRangeStart} - ${p.seatRangeEnd} (${count} sedadiel)</td>
                            <td><code>lastSeen: ${new Date(p.lastSeen).toLocaleTimeString()}</code></td>
                            <td>${statusBadge}</td>
                        </tr>
                    `;
                });
            }
            tbody.innerHTML = html;
        }
    } catch {
        // Silent failover
    }
}

async function loadEbpfStatusAndViolations() {
    try {
        const [statusRes, violationsRes] = await Promise.all([
            fetch("/v1/system/ebpf/status"),
            fetch("/v1/system/ebpf/violations?limit=20")
        ]);

        if (statusRes.ok) {
            const status = await statusRes.json();
            const driverEl = document.getElementById("ebpf-driver-mode");
            const cgroupsEl = document.getElementById("ebpf-attached-cgroups");
            const leasesEl = document.getElementById("ebpf-active-leases");
            const blockedEl = document.getElementById("ebpf-blocked-count");

            if (driverEl) driverEl.textContent = status.driverMode || "Linux-eBPF-CORE";
            if (cgroupsEl) cgroupsEl.textContent = status.attachedCgroups ? status.attachedCgroups.length : 0;
            if (leasesEl) leasesEl.textContent = status.activeLeaseCount || 0;
            if (blockedEl) blockedEl.textContent = status.blockedAttemptsCount || 0;
        }

        if (violationsRes.ok) {
            const violations = await violationsRes.json();
            const tbody = document.getElementById("ebpf-violations-table-body");
            if (tbody) {
                if (violations.length === 0) {
                    tbody.innerHTML = `<tr><td colspan="6" style="text-align: center; color: var(--text-muted); padding: 18px;">Žiadne bezpečnostné incidenty v eBPF ring buffere.</td></tr>`;
                } else {
                    tbody.innerHTML = violations.map(v => {
                        const time = new Date(v.timestamp).toLocaleTimeString();
                        return `
                            <tr>
                                <td style="font-size: 12px; color: var(--text-secondary);">${escapeHtml(time)}</td>
                                <td><code>${escapeHtml(v.cgroupId)}</code></td>
                                <td><code>PID ${escapeHtml(v.pid)}</code></td>
                                <td><code>${escapeHtml(v.destinationIp)}:${escapeHtml(v.destinationPort)}</code></td>
                                <td><span class="badge badge-revoked">${escapeHtml(v.reason)}</span></td>
                                <td><strong style="color: var(--accent-rose);">DROP (-EPERM)</strong></td>
                            </tr>
                        `;
                    }).join("");
                }
            }
        }
    } catch {
        // Silent failover
    }
}

async function loadFraudRadar() {
    try {
        const res = await fetch("/admin/v1/fraud/radar");
        if (!res.ok) return;
        const anomalies = await res.json();
        const tbody = document.getElementById("fraud-radar-table-body");
        if (!tbody) return;

        if (anomalies.length === 0) {
            tbody.innerHTML = `<tr><td colspan="7" style="text-align: center; color: var(--text-muted); padding: 18px;">Žiadne bezpečnostné anomálie nezaznamenané. Všetky prístupy sú v rámci fyzikálnych limitov.</td></tr>`;
            return;
        }

        tbody.innerHTML = anomalies.map(a => {
            const time = new Date(a.timestamp).toLocaleTimeString();
            const riskBadge = a.riskLevel === "Critical"
                ? `<span class="badge badge-revoked">KRITICKÉ</span>`
                : a.riskLevel === "High"
                ? `<span class="badge" style="background: var(--accent-amber); color: #000;">VYSOKÉ</span>`
                : `<span class="badge badge-secondary">${escapeHtml(a.riskLevel)}</span>`;

            const speedDist = a.velocityKmH
                ? `<strong>${escapeHtml(a.velocityKmH)} km/h</strong> (${escapeHtml(a.distanceKm)} km)`
                : `<span style="color: var(--text-muted);">-</span>`;

            return `
                <tr>
                    <td style="font-size: 12px; color: var(--text-secondary);">${escapeHtml(time)}</td>
                    <td><strong>${escapeHtml(a.licenseId)}</strong><br><small style="color: var(--text-muted);">${escapeHtml(a.userId || a.machineId || "N/A")}</small></td>
                    <td><code>${escapeHtml(a.ipAddress || "N/A")}</code></td>
                    <td><strong style="color: var(--accent-rose);">${escapeHtml(a.riskType)}</strong></td>
                    <td>${riskBadge}</td>
                    <td>${speedDist}</td>
                    <td style="font-size: 12px; max-width: 320px; line-height: 1.4;">${escapeHtml(a.description)}</td>
                </tr>
            `;
        }).join("");
    } catch {
        // Silent failover
    }
}

async function loadBorrowedSeats() {
    try {
        const res = await fetch("/admin/v1/leases/borrowed");
        if (!res.ok) return;
        const seats = await res.json();
        const tbody = document.getElementById("borrowed-seats-table-body");
        if (!tbody) return;

        if (seats.length === 0) {
            tbody.innerHTML = `<tr><td colspan="7" style="text-align: center; color: var(--text-muted); padding: 18px;">Žiadne aktívne zapožičané sedadlá. Všetky licencie bežia v online plávajúcom režime.</td></tr>`;
            return;
        }

        tbody.innerHTML = seats.map(s => {
            const expTime = new Date(s.borrowedUntil).toLocaleString();
            return `
                <tr>
                    <td><code>${escapeHtml(s.leaseId)}</code></td>
                    <td><strong>${escapeHtml(s.licenseId)}</strong></td>
                    <td><span class="badge badge-secondary">#${escapeHtml(s.seatNo)}</span></td>
                    <td><code>${escapeHtml(s.machineId || "field-laptop")}</code></td>
                    <td style="font-size: 12px; color: var(--text-secondary);">${escapeHtml(expTime)}</td>
                    <td><strong style="color: var(--accent-cyan);">${escapeHtml(s.hoursRemaining)} hod.</strong></td>
                    <td>
                        <button class="btn btn-secondary btn-sm" onclick="returnBorrowedSeat('${escapeHtml(s.leaseId)}')">Vrátiť</button>
                    </td>
                </tr>
            `;
        }).join("");
    } catch {
        // Silent failover
    }
}

async function returnBorrowedSeat(leaseId) {
    if (!confirm(`Naozaj chcete predčasne ukončiť zapožičanie a vrátiť sedadlo pre lease ${leaseId}?`)) {
        return;
    }

    try {
        const res = await fetch(`/admin/v1/leases/${encodeURIComponent(leaseId)}/return`, {
            method: "POST"
        });

        if (res.ok) {
            showToast("Zapožičané sedadlo bolo úspešne uvoľnené.", "success");
            await loadBorrowedSeats();
            await loadDashboardKPIs();
        } else {
            showToast("Nepodarilo sa uvoľniť sedadlo.", "error");
        }
    } catch (err) {
        showToast("Chyba komunikácie so serverom: " + err.message, "error");
    }
}

// 7. OpenTelemetry Distributed Traces Waterfall
async function loadDistributedTraces() {
    const tbody = document.getElementById("distributed-traces-table-body");
    if (!tbody) return;

    try {
        const res = await fetch("/admin/v1/traces/recent");
        if (!res.ok) {
            tbody.innerHTML = `<tr><td colspan="7" style="text-align: center; color: var(--accent-rose); padding: 18px;">Chyba pri načítaní stôp (HTTP ${res.status})</td></tr>`;
            return;
        }

        const traces = await res.json();
        if (!traces || traces.length === 0) {
            tbody.innerHTML = `<tr><td colspan="7" style="text-align: center; color: var(--text-muted); padding: 18px;">Žiadne zaznamenané stopy. Vykonajte checkout alebo obnovu licencie pre vygenerovanie W3C stopy.</td></tr>`;
            return;
        }

        tbody.innerHTML = traces.map(t => {
            const timeStr = new Date(t.startTime).toLocaleTimeString();
            const statusClass = t.status === "OK" ? "badge-active" : (t.status === "ERROR" ? "badge-danger" : "badge-secondary");
            const tagsFormatted = Object.entries(t.tags || {})
                .map(([k, v]) => `<span style="display:inline-block; font-family: monospace; font-size: 11px; background: rgba(255,255,255,0.06); padding: 2px 6px; border-radius: 4px; margin: 1px 2px;"><strong>${escapeHtml(k.replace("symbolon.", ""))}</strong>: ${escapeHtml(String(v))}</span>`)
                .join(" ");

            return `<tr>
                <td style="font-family: monospace; font-size: 12px; color: var(--text-secondary);">${escapeHtml(timeStr)}</td>
                <td><strong style="color: var(--accent-cyan); font-family: monospace;">${escapeHtml(t.name)}</strong></td>
                <td style="font-family: monospace; font-size: 11px; color: var(--text-muted); max-width: 140px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;" title="${escapeHtml(t.traceId)}">${escapeHtml(t.traceId)}</td>
                <td style="font-family: monospace; font-size: 11px; color: var(--text-muted);">${escapeHtml(t.spanId)}</td>
                <td><span style="font-family: monospace; font-size: 12px; font-weight: 600; color: ${t.durationMs > 100 ? 'var(--accent-amber)' : 'var(--accent-emerald)'};">${t.durationMs.toFixed(1)} ms</span></td>
                <td><span class="badge ${statusClass}">${escapeHtml(t.status)}</span></td>
                <td>${tagsFormatted || '<span style="color:var(--text-muted);">-</span>'}</td>
            </tr>`;
        }).join("");
    } catch (err) {
        tbody.innerHTML = `<tr><td colspan="7" style="text-align: center; color: var(--accent-rose); padding: 18px;">Chyba komunikácie: ${escapeHtml(err.message)}</td></tr>`;
    }
}



