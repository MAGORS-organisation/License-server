// Symbolon Control Plane - Web Dashboard Client

document.addEventListener("DOMContentLoaded", () => {
    initNavigation();
    initModals();
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
        loadWebhooks()
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
});


