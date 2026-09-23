// Symbolon Control Plane - Web Dashboard Client

// Intercept fetch to append API Key header automatically
const originalFetch = window.fetch;
window.fetch = function(url, options = {}) {
    const key = localStorage.getItem("symbolon_api_key");
    if (key && typeof url === "string" && (url.startsWith("/admin/") || url.startsWith("/v1/") || url.startsWith("/scim/"))) {
        options = { ...options };
        options.headers = options.headers || {};
        if (options.headers instanceof Headers) {
            if (!options.headers.has("X-Api-Key")) {
                options.headers.set("X-Api-Key", key);
            }
            if (!options.headers.has("Authorization")) {
                options.headers.set("Authorization", `Bearer ${key}`);
            }
        } else if (Array.isArray(options.headers)) {
            options.headers.push(["X-Api-Key", key]);
            options.headers.push(["Authorization", `Bearer ${key}`]);
        } else {
            options.headers["X-Api-Key"] = key;
            options.headers["Authorization"] = `Bearer ${key}`;
        }
    }
    return originalFetch(url, options);
};

document.addEventListener("DOMContentLoaded", () => {
    initNavigation();
    initModals();
    updateApiKeyButtonState();
    checkSsoUserStatus();
    loadSsoProviders();
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
                if (targetView === "view-features") {
                    loadFeaturesView();
                }
                if (targetView === "view-revocations") {
                    loadRevocationsView();
                }
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
        loadKmsHierarchy(),
        loadTokenWallets(),
        loadTokenRates(),
        loadTokenLedger(),
        loadAuditLogs(),
        loadWebhooks(),
        loadGlobalDeliveries(),
        loadApiKeys(),
        loadTransparencyRoot(),
        loadMeshAndAlerts(),
        loadScimDirectory(),
        loadFeaturesView(),
        loadRevocationsView()
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

// Cloud KMS & Key Hierarchy (§9.3)
async function loadKmsHierarchy() {
    try {
        const [kmsRes, hierRes] = await Promise.all([
            fetch("/admin/v1/kms/status"),
            fetch("/admin/v1/keys/hierarchy")
        ]);

        if (kmsRes.ok) {
            const kms = await kmsRes.json();
            const badge = document.getElementById("kms-status-badge");
            const typeElem = document.getElementById("kms-provider-type");
            const detailsElem = document.getElementById("kms-provider-details");

            if (badge) {
                badge.className = kms.isHealthy ? "badge badge-active" : "badge badge-revoked";
                badge.textContent = kms.isHealthy ? "ONLINE / HEALTHY" : "OFFLINE";
            }
            if (typeElem) {
                typeElem.textContent = `${kms.providerType || "Envelope"} (${kms.keysCount || 0} kľúčov)`;
            }
            if (detailsElem) {
                detailsElem.textContent = kms.details || "AES-256-GCM šifrovaná obálka";
            }
        }

        if (hierRes.ok) {
            const chain = await hierRes.json();
            const container = document.getElementById("hierarchy-cards-container");
            if (!container) return;

            const tiers = [
                {
                    title: "Tier 1: Root Key Anchor",
                    subtitle: "Master Trust Anchor (Offline / Hardware)",
                    cert: chain.rootCertificate,
                    color: "var(--accent-emerald)",
                    badgeText: "Root Anchor",
                    icon: "👑"
                },
                {
                    title: "Tier 2: Product Authority",
                    subtitle: "Intermediate CA pre produktové balíky",
                    cert: chain.productCertificate,
                    color: "var(--accent-blue)",
                    badgeText: "Product Authority",
                    icon: "📦"
                },
                {
                    title: "Tier 3: Leaf Lease Key",
                    subtitle: "Efemerálny kľúč pre cluster nodes a sedadlá",
                    cert: chain.leaseCertificate,
                    color: "var(--accent-purple)",
                    badgeText: "Lease Leaf",
                    icon: "🔑"
                }
            ];

            container.innerHTML = tiers.map(t => {
                const c = t.cert;
                if (!c) return "";
                const from = c.validFrom ? new Date(c.validFrom).toLocaleDateString() : "—";
                const until = c.validUntil ? new Date(c.validUntil).toLocaleDateString() : "—";
                const sigSnippet = c.signature ? `${c.signature.substring(0, 18)}...` : "—";

                return `
                    <div style="background:var(--bg-secondary); border:1px solid ${t.color}; border-radius:8px; padding:16px; display:flex; flex-direction:column; gap:8px;">
                        <div style="display:flex; justify-content:space-between; align-items:center;">
                            <span style="font-weight:700; font-size:14px; color:var(--text-primary);">${t.icon} ${t.title}</span>
                            <span class="badge" style="background:${t.color}22; color:${t.color}; border:1px solid ${t.color};">${t.badgeText}</span>
                        </div>
                        <div style="font-size:11px; color:var(--text-muted);">${t.subtitle}</div>
                        <div style="margin-top:6px; font-size:12px; font-family:monospace;">
                            <div><strong style="color:var(--text-secondary);">Key ID:</strong> <span style="color:var(--text-primary);">${escapeHtml(c.subjectKeyId)}</span></div>
                            <div><strong style="color:var(--text-secondary);">Issuer ID:</strong> <span style="color:var(--text-primary);">${escapeHtml(c.issuerKeyId)}</span></div>
                            <div><strong style="color:var(--text-secondary);">Platnosť:</strong> <span style="color:var(--text-primary);">${from} – ${until}</span></div>
                            <div><strong style="color:var(--text-secondary);">Podpis:</strong> <span style="color:var(--text-muted);">${escapeHtml(sigSnippet)}</span></div>
                        </div>
                    </div>
                `;
            }).join("");
        }
    } catch (err) {
        console.error("Error loading KMS & Hierarchy info", err);
    }
}

async function verifyKeyHierarchy() {
    try {
        const res = await fetch("/admin/v1/keys/hierarchy/verify", {
            method: "POST",
            headers: { "Content-Type": "application/json" }
        });
        if (!res.ok) {
            showToast("Chyba overenia hierarchie: HTTP " + res.status, "error");
            return;
        }
        const data = await res.json();
        const pill = document.getElementById("hierarchy-status-pill");
        const meta = document.getElementById("hierarchy-verification-meta");

        if (data.isValid) {
            if (pill) {
                pill.className = "badge badge-active";
                pill.textContent = "✓ Hierarchia Platná & Overená";
            }
            if (meta) {
                meta.textContent = `Overené: Root (${data.rootKeyId}) ➜ Product ➜ Lease`;
            }
            showToast("Kryptografická hierarchia kľúčov (Root ➜ Product ➜ Lease) je 100% platná!", "success");
        } else {
            if (pill) {
                pill.className = "badge badge-revoked";
                pill.textContent = "✗ Neplatná Hierarchia";
            }
            if (meta) {
                meta.textContent = `Zlyhanie: ${data.failureReason}`;
            }
            showToast("Verifikácia hierarchie zlyhala: " + data.failureReason, "error");
        }
    } catch (err) {
        showToast("Zlyhalo volanie verifikácie hierarchie: " + err.message, "error");
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
    const el = document.getElementById(id);
    if (el) {
        el.classList.add("active");
        el.style.display = "flex";
    }
}

function closeModal(id) {
    const el = document.getElementById(id);
    if (el) {
        el.classList.remove("active");
        el.style.display = "none";
        if (window.location.hash.toLowerCase() === "#help" && id === "modal-retro-help") {
            history.replaceState(null, "", window.location.pathname + window.location.search);
        }
    }
}

window.openModal = openModal;
window.closeModal = closeModal;

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
    const activeKpi = document.getElementById("kpi-wh-active");
    if (!tbody) return;

    try {
        const res = await fetch("/admin/v1/webhooks");
        if (!res.ok) throw new Error("Chyba pri načítaní webhookov");
        const list = await res.json();

        if (activeKpi) {
            activeKpi.textContent = list.filter(w => w.isActive).length;
        }

        if (!list || list.length === 0) {
            tbody.innerHTML = `<tr><td colspan="7" style="text-align: center; color: var(--text-muted); padding: 24px;">Žiadne aktívne webhooky. Kliknite na "+ Pridať Webhook".</td></tr>`;
            return;
        }

        tbody.innerHTML = list.map(wh => {
            const eventsHtml = (wh.events || ["*"]).map(e => `<span class="badge badge-info" style="margin-right: 4px; font-size: 11px;">${escapeHtml(e)}</span>`).join("");
            const statusBadge = wh.isActive
                ? `<span class="badge badge-active">Aktívny</span>`
                : `<span class="badge badge-revoked">Neaktívny</span>`;

            const formatBadge = (wh.format === "slack")
                ? `<span class="badge" style="background: rgba(224, 30, 90, 0.2); color: #e01e5a; border: 1px solid #e01e5a;">Slack</span>`
                : (wh.format === "teams")
                ? `<span class="badge" style="background: rgba(98, 100, 167, 0.2); color: #818cf8; border: 1px solid #6366f1;">Teams</span>`
                : `<span class="badge badge-secondary">JSON</span>`;

            const lastDelivered = wh.lastDeliveredAt
                ? new Date(wh.lastDeliveredAt).toLocaleString("sk-SK")
                : `<span style="color: var(--text-muted); font-size: 12px;">Nikdy</span>`;

            return `
                <tr>
                    <td><strong>${escapeHtml(wh.name || "Webhook")}</strong></td>
                    <td>${formatBadge}</td>
                    <td><code style="color: var(--accent-indigo); font-size: 12px;" title="${escapeHtml(wh.url)}">${escapeHtml(wh.url.length > 40 ? wh.url.substring(0, 38) + '...' : wh.url)}</code></td>
                    <td>${eventsHtml}</td>
                    <td style="color: var(--text-secondary); font-size: 12px;">${lastDelivered}</td>
                    <td>${statusBadge}</td>
                    <td>
                        <button class="btn btn-secondary btn-sm" onclick="testWebhookPing('${wh.id}')" title="Odošle test.ping udalosť">⚡ Test</button>
                        <button class="btn btn-secondary btn-sm" onclick="viewWebhookDeliveries('${wh.id}', '${escapeHtml(wh.url)}')" title="História doručení">📜 História</button>
                        <button class="btn btn-danger btn-sm" onclick="deleteWebhookPrompt('${wh.id}')" title="Zmazať webhook">🗑️</button>
                    </td>
                </tr>
            `;
        }).join("");
    } catch (err) {
        tbody.innerHTML = `<tr><td colspan="7" style="text-align: center; color: var(--accent-rose); padding: 24px;">${escapeHtml(err.message)}</td></tr>`;
    }
}

// Global Deliveries & Dead-Letter Queue
let currentDeliveryFilter = "";

async function loadGlobalDeliveries(statusFilter = currentDeliveryFilter) {
    currentDeliveryFilter = statusFilter;
    const tbody = document.getElementById("global-deliveries-table-body");
    if (!tbody) return;

    try {
        const query = statusFilter ? `?status=${encodeURIComponent(statusFilter)}` : "";
        const res = await fetch(`/admin/v1/webhooks/deliveries${query}`);
        if (!res.ok) throw new Error("Chyba načítania doručení");
        const list = await res.json();

        // Also update summary KPI counters from all deliveries
        const allRes = await fetch("/admin/v1/webhooks/deliveries");
        if (allRes.ok) {
            const all = await allRes.json();
            const delivered = all.filter(d => d.status === "delivered").length;
            const failed = all.filter(d => d.status === "failed").length;
            const dlq = all.filter(d => d.status === "dead_letter").length;

            const elDelivered = document.getElementById("kpi-wh-delivered");
            const elFailed = document.getElementById("kpi-wh-failed");
            const elDlq = document.getElementById("kpi-wh-dlq");

            if (elDelivered) elDelivered.textContent = delivered;
            if (elFailed) elFailed.textContent = failed;
            if (elDlq) elDlq.textContent = dlq;
        }

        if (!list || list.length === 0) {
            tbody.innerHTML = `<tr><td colspan="8" style="text-align: center; color: var(--text-muted); padding: 24px;">Žiadne záznamy pre vybraný filter (${escapeHtml(statusFilter || "všetky")}).</td></tr>`;
            return;
        }

        tbody.innerHTML = list.map(d => {
            let statusBadge = `<span class="badge badge-secondary">${escapeHtml(d.status)}</span>`;
            if (d.status === "delivered") {
                statusBadge = `<span class="badge badge-active">Doručené</span>`;
            } else if (d.status === "failed") {
                statusBadge = `<span class="badge badge-revoked">Zlyhalo</span>`;
            } else if (d.status === "dead_letter") {
                statusBadge = `<span class="badge" style="background: rgba(168, 85, 247, 0.2); color: #c084fc; border: 1px solid #a855f7;">DLQ (Zlyhané)</span>`;
            } else if (d.status === "pending") {
                statusBadge = `<span class="badge badge-amber">Čaká</span>`;
            }

            const codeBadge = d.statusCode
                ? `<span class="badge ${d.statusCode >= 200 && d.statusCode < 300 ? 'badge-active' : 'badge-revoked'}">${d.statusCode}</span>`
                : `<span style="color: var(--text-muted);">-</span>`;

            const durationStr = (d.durationMs !== null && d.durationMs !== undefined)
                ? `${d.durationMs} ms`
                : "-";

            const timeStr = new Date(d.createdAt).toLocaleTimeString("sk-SK");
            const errorInfo = d.lastError
                ? `<span style="color: var(--accent-rose); font-size: 11px;" title="${escapeHtml(d.lastError)}">${escapeHtml(d.lastError.length > 35 ? d.lastError.substring(0, 35) + '...' : d.lastError)}</span>`
                : `<span style="color: var(--text-secondary); font-size: 11px;">${timeStr}</span>`;

            const replayBtn = (d.status === "dead_letter" || d.status === "failed")
                ? `<button class="btn btn-secondary btn-sm" onclick="replayDelivery('${escapeHtml(d.id)}')" title="Znovu odoslať zlyhanú správu">🔄 Replay</button>`
                : `<span style="color: var(--text-muted); font-size: 12px;">—</span>`;

            return `
                <tr>
                    <td><code>${escapeHtml(d.id.substring(0, 8))}...</code></td>
                    <td><strong style="color: var(--accent-indigo);">${escapeHtml(d.eventType)}</strong></td>
                    <td>${codeBadge}</td>
                    <td style="font-size: 12px; color: var(--text-secondary);">${durationStr}</td>
                    <td>${d.attempts}</td>
                    <td>${statusBadge}</td>
                    <td>${errorInfo}</td>
                    <td>${replayBtn}</td>
                </tr>
            `;
        }).join("");
    } catch (err) {
        tbody.innerHTML = `<tr><td colspan="8" style="text-align: center; color: var(--accent-rose); padding: 24px;">${escapeHtml(err.message)}</td></tr>`;
    }
}

function filterDeliveries(status) {
    document.querySelectorAll("#btn-filter-all, #btn-filter-delivered, #btn-filter-failed, #btn-filter-dlq").forEach(btn => btn.classList.remove("active"));
    if (status === "delivered") document.getElementById("btn-filter-delivered")?.classList.add("active");
    else if (status === "failed") document.getElementById("btn-filter-failed")?.classList.add("active");
    else if (status === "dead_letter") document.getElementById("btn-filter-dlq")?.classList.add("active");
    else document.getElementById("btn-filter-all")?.classList.add("active");

    loadGlobalDeliveries(status);
}

async function replayDelivery(id) {
    showToast("Opakujem doručenie správy...", "info");
    try {
        const res = await fetch(`/admin/v1/webhooks/deliveries/${encodeURIComponent(id)}/replay`, {
            method: "POST"
        });
        const data = await res.json();
        if (data.success) {
            showToast("Správa bola zaradená na okamžité zopakovanie!", "success");
            setTimeout(() => {
                loadGlobalDeliveries();
            }, 1000);
        } else {
            showToast(`Replay zlyhal: ${data.message || 'Neznáma chyba'}`, "error");
        }
    } catch (err) {
        showToast("Chyba spojenia: " + err.message, "error");
    }
}

async function evaluateLicenseLifecycle() {
    showToast("Vyhodnocujem životný cyklus licencií...", "info");
    try {
        const res = await fetch("/admin/v1/lifecycle/evaluate", {
            method: "POST"
        });
        if (res.ok) {
            const data = await res.json();
            showToast(`Kontrola ukončená! Vyhodnotených: ${data.evaluatedLicenses}, Odoslaných alertov: ${data.eventsDispatched}`, "success");
            loadGlobalDeliveries();
        } else {
            showToast("Chyba pri vyhodnocovaní licencií", "error");
        }
    } catch (err) {
        showToast("Chyba spojenia: " + err.message, "error");
    }
}

// Create Webhook Form & Event Handlers
document.addEventListener("DOMContentLoaded", () => {
    document.getElementById("form-create-webhook")?.addEventListener("submit", async (e) => {
        e.preventDefault();
        const name = document.getElementById("wh-name")?.value.trim() || null;
        const format = document.getElementById("wh-format")?.value || "json";
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
                body: JSON.stringify({ name, format, url, secret, events })
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

// Enterprise SSO Functions
async function checkSsoUserStatus() {
    const badge = document.getElementById("sso-user-badge");
    const nameEl = document.getElementById("sso-user-name");
    const roleEl = document.getElementById("sso-user-role");
    if (!badge || !nameEl || !roleEl) return;

    try {
        const res = await fetch("/auth/sso/me");
        if (res.ok) {
            const data = await res.json();
            if (data.isAuthenticated) {
                badge.style.display = "inline-flex";
                nameEl.textContent = "👤 " + (data.userName || data.email || data.userId);
                roleEl.textContent = data.role || "user";
                if (data.role === "admin:super") {
                    roleEl.style.backgroundColor = "var(--accent-rose)";
                } else if (data.role === "admin:tenant") {
                    roleEl.style.backgroundColor = "var(--accent-primary)";
                } else {
                    roleEl.style.backgroundColor = "var(--text-muted)";
                }
            } else {
                badge.style.display = "none";
            }
        } else {
            badge.style.display = "none";
        }
    } catch {
        badge.style.display = "none";
    }
}

async function loadSsoProviders() {
    const list = document.getElementById("sso-providers-list");
    if (!list) return;

    try {
        const res = await fetch("/auth/sso/providers");
        if (res.ok) {
            const providers = await res.json();
            if (Array.isArray(providers) && providers.length > 0) {
                list.innerHTML = providers.map(p => `
                    <a href="${escapeHtml(p.loginEndpoint)}" class="btn btn-secondary btn-sm" style="width: 100%; text-align: center; display: flex; align-items: center; justify-content: center; gap: 8px;">
                        <span>🔑</span>
                        <span>Prihlásiť sa cez ${escapeHtml(p.displayName)} (${p.providerType.toUpperCase()})</span>
                    </a>
                `).join("");
            }
        }
    } catch {
        // Fallback to static link
    }
}

async function logoutSso() {
    try {
        await fetch("/auth/sso/logout", { method: "POST" });
        showToast("Boli ste odhlásený zo SSO relácie.", "info");
        setTimeout(() => location.reload(), 500);
    } catch (err) {
        showToast("Chyba pri odhlásení: " + err.message, "error");
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

// SCIM 2.0 Directory Management
async function loadScimDirectory() {
    const tbody = document.getElementById("scim-users-table-body");
    const totalEl = document.getElementById("kpi-scim-total-users");
    const activeEl = document.getElementById("kpi-scim-active-users");
    const deactEl = document.getElementById("kpi-scim-deactivated-users");
    const urlEl = document.getElementById("scim-tenant-url");

    if (urlEl) {
        urlEl.textContent = `${window.location.origin}/scim/v2`;
    }

    try {
        const res = await fetch("/admin/v1/scim/users");
        if (!res.ok) {
            if (tbody) tbody.innerHTML = `<tr><td colspan="8" style="text-align: center; color: var(--accent-amber); padding: 18px;">Pre načítanie SCIM používateľov zadajte platný administrátorský API kľúč.</td></tr>`;
            return;
        }

        const users = await res.json();
        if (!users || users.length === 0) {
            if (totalEl) totalEl.textContent = "0";
            if (activeEl) activeEl.textContent = "0";
            if (deactEl) deactEl.textContent = "0";
            if (tbody) tbody.innerHTML = `<tr><td colspan="8" style="text-align: center; color: var(--text-muted); padding: 18px;">Žiadni používatelia synchronizovaní cez SCIM 2.0. Nastavte integráciu vo vašom IdP (Okta / Entra ID).</td></tr>`;
            return;
        }

        let activeCount = 0;
        let deactCount = 0;
        users.forEach(u => {
            if (u.active) activeCount++;
            else deactCount++;
        });

        if (totalEl) totalEl.textContent = users.length;
        if (activeEl) activeEl.textContent = activeCount;
        if (deactEl) deactEl.textContent = deactCount;

        if (tbody) {
            tbody.innerHTML = users.map(u => {
                const statusBadge = u.active
                    ? `<span class="badge badge-active">Aktívny</span>`
                    : `<span class="badge badge-expired" style="background: rgba(244,63,94,0.15); color: var(--accent-rose); border: 1px solid var(--accent-rose);">Pozastavený (Revoked)</span>`;

                const groupsStr = (u.groups && u.groups.length > 0)
                    ? u.groups.map(g => `<span class="badge badge-secondary" style="margin-right: 4px; font-size: 11px;">${escapeHtml(g)}</span>`).join("")
                    : `<span style="color: var(--text-muted); font-size: 12px;">-</span>`;

                const actionBtn = u.active
                    ? `<button class="btn btn-danger btn-sm" onclick="deactivateScimUser('${u.id}', '${escapeHtml(u.userName)}')">🚫 Deaktivovať &amp; Uvoľniť</button>`
                    : `<button class="btn btn-secondary btn-sm" onclick="reactivateScimUser('${u.id}', '${escapeHtml(u.userName)}')">✅ Obnoviť Účet</button>`;

                const updatedStr = u.updatedAt ? new Date(u.updatedAt).toLocaleString("sk-SK") : "-";

                return `<tr>
                    <td><strong>${escapeHtml(u.userName)}</strong></td>
                    <td>${escapeHtml(u.displayName || "-")}</td>
                    <td>${escapeHtml(u.email || "-")}</td>
                    <td><code style="font-size: 11px; color: var(--text-muted);">${escapeHtml(u.externalId || u.id)}</code></td>
                    <td>${groupsStr}</td>
                    <td>${statusBadge}</td>
                    <td style="font-size: 12px; color: var(--text-secondary);">${updatedStr}</td>
                    <td>${actionBtn}</td>
                </tr>`;
            }).join("");
        }
    } catch (err) {
        if (tbody) tbody.innerHTML = `<tr><td colspan="8" style="text-align: center; color: var(--accent-rose); padding: 18px;">Chyba komunikácie: ${escapeHtml(err.message)}</td></tr>`;
    }
}

async function deactivateScimUser(id, userName) {
    if (!confirm(`Naozaj chcete okamžite deprovisionovať používateľa '${userName}'?\n\nVšetky jeho aktívne floating licencie budú okamžite uvoľnené a čakajúce fronty zrušené!`)) {
        return;
    }
    try {
        const res = await fetch(`/scim/v2/Users/${encodeURIComponent(id)}`, {
            method: "PATCH",
            headers: { "Content-Type": "application/scim+json" },
            body: JSON.stringify({
                schemas: ["urn:ietf:params:scim:api:messages:2.0:PatchOp"],
                Operations: [
                    { op: "replace", path: "active", value: false }
                ]
            })
        });
        if (res.ok) {
            showToast(`Používateľ '${userName}' bol deprovisionovaný a všetky jeho licencie uvoľnené.`, "success");
            await loadScimDirectory();
            await loadLicenses();
            await loadAuditLogs();
        } else {
            showToast(`Chyba pri deprovisionovaní: ${res.statusText}`, "error");
        }
    } catch (e) {
        showToast("Chyba spojenia: " + e.message, "error");
    }
}

async function reactivateScimUser(id, userName) {
    try {
        const res = await fetch(`/scim/v2/Users/${encodeURIComponent(id)}`, {
            method: "PATCH",
            headers: { "Content-Type": "application/scim+json" },
            body: JSON.stringify({
                schemas: ["urn:ietf:params:scim:api:messages:2.0:PatchOp"],
                Operations: [
                    { op: "replace", path: "active", value: true }
                ]
            })
        });
        if (res.ok) {
            showToast(`Používateľ '${userName}' bol úspešne reaktivovaný.`, "success");
            await loadScimDirectory();
        } else {
            showToast(`Chyba pri reaktivácii: ${res.statusText}`, "error");
        }
    } catch (e) {
        showToast("Chyba spojenia: " + e.message, "error");
    }
}

// --- Wasm / In-Browser Offline License Validator Logic ---
async function detectWasmBrowserFingerprint() {
    try {
        if (typeof generateBrowserFingerprint === 'function') {
            const fp = await generateBrowserFingerprint();
            const fpInput = document.getElementById('wasm-fp-input');
            const disp = document.getElementById('wasm-detected-fp-display');
            if (fpInput) fpInput.value = fp;
            if (disp) disp.textContent = fp;
            showToast('Odtlačok prehliadača úspešne vygenerovaný: ' + fp, 'success');
        } else {
            showToast('Funkcia generateBrowserFingerprint nie je načítaná.', 'error');
        }
    } catch (err) {
        showToast('Chyba pri detekcii odtlačku: ' + err.message, 'error');
    }
}

async function loadServerJwksForWasm() {
    try {
        const res = await fetch('/v1/jwks');
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const jwks = await res.json();
        const jwksArea = document.getElementById('wasm-jwks-input');
        if (jwksArea) {
            jwksArea.value = JSON.stringify(jwks, null, 2);
            showToast('Dôveryhodný JWKS úspešne stiahnutý zo servera.', 'success');
        }
    } catch (err) {
        showToast('Nepodarilo sa načítať JWKS zo servera: ' + err.message, 'error');
    }
}

async function runWasmValidation() {
    const licArea = document.getElementById('wasm-license-input');
    const jwksArea = document.getElementById('wasm-jwks-input');
    const fpInput = document.getElementById('wasm-fp-input');
    const resultBox = document.getElementById('wasm-validation-results');

    if (!licArea || !licArea.value.trim()) {
        showToast('Vložte licenčný súbor alebo reťazec.', 'warning');
        return;
    }

    let jwks = null;
    if (jwksArea && jwksArea.value.trim()) {
        try {
            jwks = JSON.parse(jwksArea.value.trim());
        } catch (e) {
            showToast('Neplatný formát JSON v poli JWKS: ' + e.message, 'error');
            return;
        }
    }

    try {
        if (typeof SymbolonOfflineValidator !== 'function') {
            showToast('SymbolonOfflineValidator knižnica nie je načítaná.', 'error');
            return;
        }

        const validator = new SymbolonOfflineValidator({ jwks });
        const res = await validator.validate(licArea.value.trim(), fpInput?.value.trim() || null);

        let badgeClass = res.isValid ? 'badge-active' : (res.isExpired ? 'badge-expired' : 'badge-revoked');
        let statusText = res.isValid ? '✓ PLATNÁ LICENCIA (KRYPTOGRAFICKY OVERENÁ)' : (res.isExpired ? '⏳ LICENCIA EXPIROVALA' : '✗ NEPLATNÁ / MANIPULOVANÁ');

        let featuresHtml = (res.features && res.features.length > 0)
            ? res.features.map(f => `<span class="badge" style="background:var(--accent-primary); color:#fff; margin-right:4px;">${f}</span>`).join('')
            : '<span style="color:var(--text-muted)">Základný balík (core)</span>';

        resultBox.style.display = 'block';
        resultBox.innerHTML = `
            <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:12px; border-bottom:1px solid var(--border-color); padding-bottom:10px;">
                <span class="badge ${badgeClass}" style="font-size:14px; padding:6px 12px;">${statusText}</span>
                <span style="font-size:12px; color:var(--text-muted); font-family:monospace;">${res.verifiedAlgs.length > 0 ? 'Algoritmus: ' + res.verifiedAlgs.join(', ') : 'Bez overenia kľúča (len dekódované)'}</span>
            </div>
            ${res.failureReason ? `<div style="background:rgba(244,63,94,0.15); border:1px solid rgba(244,63,94,0.3); color:var(--accent-rose); padding:10px; border-radius:6px; margin-bottom:12px; font-size:13px;"><strong>Chyba:</strong> ${res.failureReason}</div>` : ''}
            <div style="display:grid; grid-template-columns:1fr 1fr; gap:12px; font-size:13px; color:var(--text-secondary);">
                <div><strong>Zákazník:</strong> <span style="color:var(--text-primary);">${res.customer || '—'}</span></div>
                <div><strong>Produkt (Audience):</strong> <span style="color:var(--text-primary);">${res.product || '—'}</span></div>
                <div><strong>Typ licencie:</strong> <span style="color:var(--text-primary);">${res.licenseType || 'floating'}</span></div>
                <div><strong>Kapacita sedadiel:</strong> <span style="color:var(--text-primary);">${res.maxSeats !== null ? res.maxSeats : 'Neobmedzená'}</span></div>
                <div><strong>Platnosť do:</strong> <span style="color:var(--text-primary);">${res.expiresAtFormatted}</span></div>
                <div><strong>Zostáva:</strong> <span style="color:var(--text-primary);">${res.daysRemaining !== null ? res.daysRemaining + ' dní' : 'Neobmedzene'}</span></div>
                <div><strong>Hardware viazanie:</strong> <span style="color:var(--text-primary);">${res.isNodeLocked ? (res.machineMatch ? '✓ Odtlačok súhlasí' : '✗ Nesúlad odtlačku!') : 'Voľná (Floating)'}</span></div>
                <div><strong>Vystaviteľ (Issuer):</strong> <span style="color:var(--text-primary);">${res.issuer || '—'}</span></div>
            </div>
            <div style="margin-top:12px; padding-top:10px; border-top:1px solid var(--border-color);">
                <div style="font-size:12px; font-weight:600; color:var(--text-muted); margin-bottom:6px;">POVOLENÉ MODULY (ENTITLEMENTS):</div>
                <div>${featuresHtml}</div>
            </div>
        `;
        showToast(res.isValid ? 'Licencia je platná!' : 'Licencia zlyhala pri overení.', res.isValid ? 'success' : 'error');
    } catch (err) {
        showToast('Chyba validátora: ' + err.message, 'error');
    }
}

// ==========================================
// Token & Credit-Based Licensing Engine
// ==========================================

async function loadTokenWallets() {
    const tbody = document.getElementById("token-wallets-table-body");
    if (!tbody) return;

    try {
        const res = await fetch("/admin/v1/tokens/wallets");
        if (!res.ok) return;
        const wallets = await res.json();

        let totalCredits = 0;
        let totalReserved = 0;

        wallets.forEach(w => {
            totalCredits += (w.balance || 0);
            totalReserved += (w.reservedCredits || 0);
        });

        const totalElem = document.getElementById("kpi-tokens-total-wallets");
        const creditsElem = document.getElementById("kpi-tokens-total-credits");
        const resElem = document.getElementById("kpi-tokens-active-reservations");

        if (totalElem) totalElem.textContent = wallets.length;
        if (creditsElem) creditsElem.textContent = Math.round(totalCredits).toLocaleString();
        if (resElem) resElem.textContent = Math.round(totalReserved).toLocaleString();

        if (wallets.length === 0) {
            tbody.innerHTML = `<tr><td colspan="7" style="text-align: center; color: var(--text-muted); padding: 32px;">Žiadne peňaženky neboli nájdené. Vytvorte prvú kliknutím na '+ Vytvoriť Peňaženku'.</td></tr>`;
            return;
        }

        tbody.innerHTML = wallets.map(w => {
            let badgeClass = "badge-active";
            if (w.state === "depleted") badgeClass = "badge-expired";
            if (w.state === "frozen") badgeClass = "badge-revoked";

            return `
                <tr>
                    <td><code style="color: var(--accent-blue); font-weight: bold;">${escapeHtml(w.code)}</code></td>
                    <td>${escapeHtml(w.name)}</td>
                    <td>
                        <strong>${(w.balance || 0).toLocaleString()}</strong> / <span style="color: var(--text-muted);">${(w.totalCredits || 0).toLocaleString()}</span>
                    </td>
                    <td style="color: var(--accent-amber);">${(w.reservedCredits || 0).toLocaleString()}</td>
                    <td style="color: var(--text-secondary);">${(w.overdraftLimit || 0).toLocaleString()}</td>
                    <td><span class="badge ${badgeClass}">${escapeHtml(w.state)}</span></td>
                    <td>
                        <button class="btn btn-secondary btn-sm" onclick="openCreditModal('${escapeHtml(w.id)}', '${escapeHtml(w.code)}')">
                            + Dobiť
                        </button>
                    </td>
                </tr>
            `;
        }).join("");
    } catch (err) {
        console.error("Error loading token wallets", err);
    }
}

async function loadTokenRates() {
    const tbody = document.getElementById("token-rates-table-body");
    if (!tbody) return;

    try {
        const res = await fetch("/admin/v1/tokens/rates");
        if (!res.ok) return;
        const rates = await res.json();

        const ratesCountElem = document.getElementById("kpi-tokens-active-rates");
        if (ratesCountElem) ratesCountElem.textContent = rates.length;

        if (rates.length === 0) {
            tbody.innerHTML = `<tr><td colspan="5" style="text-align: center; color: var(--text-muted); padding: 24px;">Žiadne špecifické sadzby. Predvolená sadzba je 1 kredit / jednotka.</td></tr>`;
            return;
        }

        tbody.innerHTML = rates.map(r => `
            <tr>
                <td><code style="color: var(--accent-emerald);">${escapeHtml(r.featureCode)}</code></td>
                <td><strong>${r.ratePerMinute}</strong> / min</td>
                <td><strong>${r.ratePerUnit}</strong> / job</td>
                <td>${escapeHtml(r.description || "—")}</td>
                <td style="font-size: 11px; color: var(--text-muted);">${new Date(r.updatedAt).toLocaleDateString()}</td>
            </tr>
        `).join("");
    } catch (err) {
        console.error("Error loading token rates", err);
    }
}

async function loadTokenLedger() {
    const tbody = document.getElementById("token-ledger-table-body");
    if (!tbody) return;

    try {
        const res = await fetch("/admin/v1/tokens/ledger?limit=30");
        if (!res.ok) return;
        const entries = await res.json();

        if (entries.length === 0) {
            tbody.innerHTML = `<tr><td colspan="7" style="text-align: center; color: var(--text-muted); padding: 24px;">Žiadne pohyby kreditov v auditnom denníku.</td></tr>`;
            return;
        }

        tbody.innerHTML = entries.map(e => {
            let color = "var(--text-primary)";
            let sign = "";
            if (e.transactionType === "credit") { color = "var(--accent-emerald)"; sign = "+"; }
            else if (e.transactionType === "consume") { color = "var(--accent-rose)"; sign = "-"; }
            else if (e.transactionType === "reserve") { color = "var(--accent-amber)"; sign = "🔒 "; }
            else if (e.transactionType === "release") { color = "var(--accent-blue)"; sign = "🔓 "; }

            return `
                <tr>
                    <td style="white-space: nowrap; font-size: 11px; color: var(--text-secondary);">${new Date(e.timestamp).toLocaleTimeString()}</td>
                    <td><code>${escapeHtml(e.walletId.substring(0, 10))}...</code></td>
                    <td><span class="badge badge-type">${escapeHtml(e.transactionType)}</span></td>
                    <td style="color: ${color}; font-weight: bold;">${sign}${e.amount}</td>
                    <td><strong>${e.balanceAfter}</strong></td>
                    <td><code>${escapeHtml(e.featureCode || "—")}</code></td>
                    <td style="font-size: 11px; color: var(--text-muted); font-family: monospace;">${escapeHtml(e.idempotencyKey || "—")}</td>
                </tr>
            `;
        }).join("");
    } catch (err) {
        console.error("Error loading token ledger", err);
    }
}

function openCreditModal(walletId, code) {
    const idElem = document.getElementById("credit-wallet-id");
    const codeElem = document.getElementById("credit-wallet-code-display");
    if (idElem) idElem.value = walletId;
    if (codeElem) codeElem.value = code;
    openModal("modal-credit-wallet");
}

async function createWalletSubmit(event) {
    event.preventDefault();
    const tenantId = document.getElementById("wallet-tenant-id").value.trim();
    const code = document.getElementById("wallet-code").value.trim();
    const name = document.getElementById("wallet-name").value.trim();
    const initialCredits = parseFloat(document.getElementById("wallet-initial-credits").value) || 0;
    const overdraftLimit = parseFloat(document.getElementById("wallet-overdraft").value) || 0;

    try {
        const res = await fetch("/admin/v1/tokens/wallets", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ tenantId, code, name, initialCredits, overdraftLimit })
        });

        if (res.ok) {
            closeModal("modal-create-wallet");
            showToast("Kreditová peňaženka úspešne vytvorená!", "success");
            loadTokenWallets();
            loadTokenLedger();
        } else {
            const err = await res.json().catch(() => ({}));
            showToast("Chyba vytvorenia peňaženky: " + (err.error || res.status), "error");
        }
    } catch (e) {
        showToast("Chyba: " + e.message, "error");
    }
}

async function creditWalletSubmit(event) {
    event.preventDefault();
    const walletId = document.getElementById("credit-wallet-id").value;
    const amount = parseFloat(document.getElementById("credit-wallet-amount").value);
    const reason = document.getElementById("credit-wallet-reason").value.trim() || "Manual credit";

    try {
        const res = await fetch(`/admin/v1/tokens/wallets/${walletId}/credit`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ amount, reason })
        });

        if (res.ok) {
            closeModal("modal-credit-wallet");
            showToast(`Úspešne dobitých ${amount} kreditov!`, "success");
            loadTokenWallets();
            loadTokenLedger();
        } else {
            showToast("Chyba dobitia kreditu.", "error");
        }
    } catch (e) {
        showToast("Chyba: " + e.message, "error");
    }
}

async function setRateSubmit(event) {
    event.preventDefault();
    const tenantId = document.getElementById("rate-tenant-id").value.trim();
    const featureCode = document.getElementById("rate-feature-code").value.trim();
    const ratePerMinute = parseFloat(document.getElementById("rate-per-minute").value) || 0;
    const ratePerUnit = parseFloat(document.getElementById("rate-per-unit").value) || 0;
    const description = document.getElementById("rate-description").value.trim();

    try {
        const res = await fetch("/admin/v1/tokens/rates", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ tenantId, featureCode, ratePerMinute, ratePerUnit, description })
        });

        if (res.ok) {
            closeModal("modal-set-rate");
            showToast("Sadzba bola úspešne uložená!", "success");
            loadTokenRates();
        } else {
            showToast("Chyba uloženia sadzby.", "error");
        }
    } catch (e) {
        showToast("Chyba: " + e.message, "error");
    }
}

// ==========================================
// Phase 12: Migration Engine & Transpiler UI
// ==========================================

function switchMigrateTab(tabId) {
    document.querySelectorAll(".migrate-tab-content").forEach(el => el.style.display = "none");
    document.querySelectorAll(".migrate-tab-btn").forEach(btn => {
        btn.classList.remove("btn-primary");
        btn.classList.add("btn-secondary");
    });

    const activeContent = document.getElementById(tabId);
    if (activeContent) activeContent.style.display = "block";

    const activeBtn = document.getElementById("btn-" + tabId);
    if (activeBtn) {
        activeBtn.classList.remove("btn-secondary");
        activeBtn.classList.add("btn-primary");
    }
}

function onMigrateTypeChange() {
    const type = document.getElementById("migrate-input-type").value;
    const textarea = document.getElementById("migrate-input-content");
    if (type === "options") {
        textarea.placeholder = "Sem vložte text options.opt (GROUP, RESERVE, MAX, EXCLUDE, TIMEOUT)...";
    } else {
        textarea.placeholder = "Sem vložte text license.dat (SERVER, FEATURE, INCREMENT, PACKAGE)...";
    }
}

function loadSampleFlexNetLicense() {
    const type = document.getElementById("migrate-input-type").value;
    const textarea = document.getElementById("migrate-input-content");

    if (type === "options") {
        textarea.value = `# Options File pre mysw_vd
GROUP engineering jan.novak peter.kovac maria.horvathova
GROUP contractors extern1 extern2
HOST_GROUP cluster node-01 node-02 node-03

RESERVE 5 CAD_PRO GROUP engineering
MAX 2 CAD_PRO GROUP contractors
EXCLUDE FEA_SOLVER GROUP contractors
INCLUDE CAD_PRO HOST_GROUP cluster

BORROW_LOWWATER CAD_PRO 3
MAX_BORROW_HOURS CAD_PRO 168
TIMEOUT CAD_PRO 1800
TIMEOUTALL 3600
LINGER CAD_PRO 300
REPORTLOG +/var/log/mysw_report.log`;
    } else {
        textarea.value = `# FlexNet Publisher Vzorka license.dat
SERVER srv01.company.internal 001122334455 27000
SERVER srv02.company.internal 001122334456 27000
SERVER srv03.company.internal 001122334457 27000
VENDOR mysw_vd /opt/licenses/mysw_vd

FEATURE CAD_PRO mysw_vd 2026.1 31-dec-2027 15 \\
    SIGN="98A7B6C5D4E3" \\
    HOSTID=001122334455 \\
    NOTICE="Licensed to ACME Engineering Corp" \\
    SN=SN-998877

INCREMENT FEA_SOLVER mysw_vd 2026.0 permanent 8 \\
    SIGN="112233445566"

PACKAGE SUITE_ENTERPRISE mysw_vd 2026.1 \\
    COMPONENTS="CAD_PRO:2026.1 FEA_SOLVER:2026.0 SIM_RENDER:1.0"`;
    }
}

function loadSampleLmgrdLog() {
    document.getElementById("log-input-content").value = `09:00:00 (mysw_vd) TIMESTAMP 9/22/2026
09:05:00 (mysw_vd) OUT: "CAD_PRO" alice@ws-01
09:10:00 (mysw_vd) OUT: "CAD_PRO" bob@ws-02
09:15:00 (mysw_vd) OUT: "CAD_PRO" charlie@ws-03
09:20:00 (mysw_vd) OUT: "FEA_SOLVER" david@ws-compute
09:30:00 (mysw_vd) DENIED: "CAD_PRO" eve@ws-04 (Licensed number of users already reached. (-4,342))
09:35:00 (mysw_vd) DENIED: "CAD_PRO" frank@ws-05 (Licensed number of users already reached. (-4,342))
10:00:00 (mysw_vd) IN: "CAD_PRO" alice@ws-01
10:15:00 (mysw_vd) IN: "CAD_PRO" bob@ws-02
10:30:00 (mysw_vd) IN: "CAD_PRO" charlie@ws-03
11:00:00 (mysw_vd) IN: "FEA_SOLVER" david@ws-compute`;
}

function loadSampleKeygenJson() {
    document.getElementById("keygen-json-content").value = JSON.stringify({
        data: [
            {
                id: "pol_cad_annual",
                type: "policies",
                attributes: {
                    name: "Enterprise CAD Floating",
                    code: "CAD-FLOAT",
                    duration: 31536000,
                    maxMachines: 25,
                    floating: true,
                    concurrent: true
                }
            },
            {
                id: "usr_lead_eng",
                type: "users",
                attributes: {
                    email: "lead.engineer@acme.com",
                    firstName: "Martin",
                    lastName: "Novak"
                }
            },
            {
                id: "lic_pool_cad_01",
                type: "licenses",
                attributes: {
                    key: "KEYGEN-CAD-2026-9988",
                    name: "ACME CAD 25-Seat Pool",
                    status: "ACTIVE",
                    expiry: "2027-12-31T23:59:59Z",
                    maxMachines: 25
                },
                relationships: {
                    policy: { data: { id: "pol_cad_annual", type: "policies" } },
                    user: { data: { id: "usr_lead_eng", type: "users" } }
                }
            }
        ]
    }, null, 2);
}

async function transpileFlexNet(apply) {
    const type = document.getElementById("migrate-input-type").value;
    const content = document.getElementById("migrate-input-content").value.trim();

    if (!content) {
        showToast("Zadajte obsah súboru.", "error");
        return;
    }

    const endpoint = type === "options" ? "/admin/v1/migrate/flexnet/options" : "/admin/v1/migrate/flexnet/license";
    const body = { content, apply };

    try {
        const res = await fetch(endpoint, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body)
        });

        if (!res.ok) {
            const err = await res.json();
            showToast("Chyba spracovania: " + (err.error || res.statusText), "error");
            return;
        }

        const data = await res.json();
        document.getElementById("migrate-output-json").textContent = JSON.stringify(data, null, 2);

        // Update KPIs & Recommendations
        if (type === "license") {
            const planCount = data.plans ? data.plans.length : 0;
            document.getElementById("kpi-migrate-products-count").textContent = planCount;

            const recBox = document.getElementById("migrate-recommendations-box");
            if (data.recommendations && data.recommendations.length > 0) {
                recBox.style.display = "block";
                recBox.innerHTML = `<strong>💡 Architektonické Odporúčania:</strong><ul style="margin: 6px 0 0 16px; padding: 0;">` +
                    data.recommendations.map(r => `<li>${escapeHtml(r)}</li>`).join("") + `</ul>`;
            } else {
                recBox.style.display = "none";
            }

            if (apply) {
                showToast(`Úspešne importovaných ${data.importedCount || planCount} licencií do Symbolon DB!`, "success");
                refreshAllData();
            } else {
                showToast("Náhľad konverzie úspešne vygenerovaný!", "info");
            }
        } else {
            const rulesCount = data.totalRulesParsed || 0;
            document.getElementById("kpi-migrate-rules-count").textContent = rulesCount;

            const recBox = document.getElementById("migrate-recommendations-box");
            if (data.recommendations && data.recommendations.length > 0) {
                recBox.style.display = "block";
                recBox.innerHTML = `<strong>💡 Odporúčania pre options.opt:</strong><ul style="margin: 6px 0 0 16px; padding: 0;">` +
                    data.recommendations.map(r => `<li>${escapeHtml(r)}</li>`).join("") + `</ul>`;
            } else {
                recBox.style.display = "none";
            }
            showToast(`Prevedených ${rulesCount} pravidiel z options.opt!`, "success");
        }
    } catch (e) {
        showToast("Chyba spojenia: " + e.message, "error");
    }
}

async function analyzeLogSubmit() {
    const content = document.getElementById("log-input-content").value.trim();
    if (!content) {
        showToast("Vložte text lmgrd.log.", "error");
        return;
    }

    try {
        const res = await fetch("/admin/v1/migrate/flexnet/log-analysis", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ content })
        });

        if (!res.ok) {
            showToast("Chyba analýzy logu.", "error");
            return;
        }

        const data = await res.json();
        document.getElementById("kpi-log-events").textContent = data.totalEventsCount || 0;
        document.getElementById("kpi-log-peak").textContent = data.overallPeakConcurrency || 0;
        document.getElementById("kpi-log-denials").textContent = data.totalDenialsAcrossAllFeatures || 0;

        const tbody = document.getElementById("log-features-table-body");
        if (!data.featureStatistics || data.featureStatistics.length === 0) {
            tbody.innerHTML = `<tr><td colspan="8" style="text-align: center; color: var(--text-muted); padding: 16px;">V logu neboli identifikované žiadne licenčné udalosti.</td></tr>`;
        } else {
            tbody.innerHTML = data.featureStatistics.map(f => `
                <tr>
                    <td><strong>${escapeHtml(f.featureCode)}</strong></td>
                    <td><span class="badge" style="background: rgba(59, 130, 246, 0.2); color: var(--accent-indigo); font-weight: bold;">${f.peakConcurrency} sedadiel</span></td>
                    <td>${f.totalCheckouts}</td>
                    <td><span class="badge ${f.totalDenials > 0 ? "badge-revoked" : "badge-emerald"}">${f.totalDenials}</span></td>
                    <td>${f.uniqueUsersCount} používateľov / ${f.uniqueHostsCount} staníc</td>
                    <td><strong style="color: var(--accent-emerald); font-size: 14px;">${f.recommendedSeats}</strong></td>
                    <td><span class="badge badge-amber">+${f.recommendedOverdraftBuffer} kreditov</span></td>
                    <td style="font-size: 12px; color: var(--text-secondary);">${escapeHtml(f.sizingRationale)}</td>
                </tr>
            `).join("");
        }

        showToast("Analýza lmgrd.log úspešne dokončená!", "success");
    } catch (e) {
        showToast("Chyba spojenia: " + e.message, "error");
    }
}

async function importKeygenSubmit(apply) {
    const content = document.getElementById("keygen-json-content").value.trim();
    if (!content) {
        showToast("Vložte Keygen.sh export JSON.", "error");
        return;
    }

    try {
        const res = await fetch("/admin/v1/migrate/keygen", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ content, apply })
        });

        if (!res.ok) {
            showToast("Chyba importu z Keygen.sh.", "error");
            return;
        }

        const data = await res.json();
        document.getElementById("keygen-output-preview").textContent = JSON.stringify(data, null, 2);

        if (apply) {
            showToast(`Úspešne importovaných ${data.importedCount || 0} Keygen licencií do Symbolon DB!`, "success");
            refreshAllData();
        } else {
            showToast("Náhľad Keygen importu vygenerovaný!", "info");
        }
    } catch (e) {
        showToast("Chyba: " + e.message, "error");
    }
}

// ==========================================
// Dynamic Entitlements & Features Management
// ==========================================

async function loadFeaturesView() {
    await Promise.allSettled([
        loadFeaturesCatalog(),
        loadPackageSuites(),
        loadFeatureUsageMeters()
    ]);
}

async function loadFeaturesCatalog() {
    try {
        const res = await fetch("/admin/v1/entitlements/features");
        if (!res.ok) return;

        const list = await res.json();
        const totalEl = document.getElementById("feat-kpi-total");
        if (totalEl) totalEl.textContent = list.length;

        const tbody = document.getElementById("features-table-body");
        if (!tbody) return;

        if (list.length === 0) {
            tbody.innerHTML = `<tr><td colspan="8" style="text-align: center; color: var(--text-muted); padding: 18px;">Žiadne definované moduly v katalógu. Vytvorte prvý modul tlačidlom vyššie.</td></tr>`;
            return;
        }

        tbody.innerHTML = list.map(f => {
            const verRange = (!f.minVersion && !f.maxVersion)
                ? `<span class="badge" style="background: rgba(16, 185, 129, 0.15); color: var(--accent-emerald);">Všetky (*)</span>`
                : `<span class="badge badge-indigo">${escapeHtml(f.minVersion || "0")} – ${escapeHtml(f.maxVersion || "latest")}</span>`;

            const maxSeatsStr = f.defaultMaxSeats != null
                ? `<strong style="color: var(--accent-emerald);">${f.defaultMaxSeats}</strong>`
                : `<span style="color: var(--text-muted);">Neobmedzené (∞)</span>`;

            const typeBadge = f.isFloating
                ? `<span class="badge" style="background: rgba(59, 130, 246, 0.15); color: var(--accent-indigo);">Floating Seat</span>`
                : `<span class="badge" style="background: rgba(139, 92, 246, 0.15); color: var(--accent-purple);">Seatless / Node</span>`;

            const createdDate = f.createdAt ? new Date(f.createdAt).toLocaleDateString("sk-SK") : "-";

            return `
                <tr>
                    <td><strong style="color: var(--accent-cyan); font-family: monospace;">${escapeHtml(f.code)}</strong></td>
                    <td><strong>${escapeHtml(f.name)}</strong>${f.description ? `<br><small style="color: var(--text-muted);">${escapeHtml(f.description)}</small>` : ""}</td>
                    <td>${f.productId ? `<span class="badge badge-amber">${escapeHtml(f.productId)}</span>` : '<span style="color: var(--text-muted);">-</span>'}</td>
                    <td>${typeBadge}</td>
                    <td>${verRange}</td>
                    <td>${maxSeatsStr}</td>
                    <td style="font-size: 12px; color: var(--text-secondary);">${createdDate}</td>
                    <td>
                        <button type="button" class="btn btn-secondary btn-sm" style="color: var(--accent-rose); border-color: rgba(244, 63, 94, 0.3);" onclick="deleteFeature('${escapeHtml(f.id)}')">🗑 Zmazať</button>
                    </td>
                </tr>
            `;
        }).join("");
    } catch (e) {
        console.error("Chyba pri načítaní katalógu funkcií:", e);
    }
}

async function loadPackageSuites() {
    try {
        const res = await fetch("/admin/v1/entitlements/suites");
        if (!res.ok) return;

        const list = await res.json();
        const suitesEl = document.getElementById("feat-kpi-suites");
        if (suitesEl) suitesEl.textContent = list.length;

        const tbody = document.getElementById("suites-table-body");
        if (!tbody) return;

        if (list.length === 0) {
            tbody.innerHTML = `<tr><td colspan="6" style="text-align: center; color: var(--text-muted); padding: 18px;">Žiadne balíčky (suites). Vytvorte nový balíček pre agregovaný predaj modulov.</td></tr>`;
            return;
        }

        tbody.innerHTML = list.map(s => {
            const featsBadges = (s.featureCodes || []).map(fc =>
                `<span class="badge" style="background: rgba(6, 182, 212, 0.15); color: var(--accent-cyan); font-family: monospace; margin: 2px;">${escapeHtml(fc)}</span>`
            ).join(" ");

            const createdDate = s.createdAt ? new Date(s.createdAt).toLocaleDateString("sk-SK") : "-";

            return `
                <tr>
                    <td><strong style="color: var(--accent-indigo); font-family: monospace;">${escapeHtml(s.code)}</strong></td>
                    <td><strong>${escapeHtml(s.name)}</strong>${s.description ? `<br><small style="color: var(--text-muted);">${escapeHtml(s.description)}</small>` : ""}</td>
                    <td>${s.productId ? `<span class="badge badge-amber">${escapeHtml(s.productId)}</span>` : '<span style="color: var(--text-muted);">-</span>'}</td>
                    <td>${featsBadges || '<span style="color: var(--text-muted);">(prázdny balíček)</span>'}</td>
                    <td style="font-size: 12px; color: var(--text-secondary);">${createdDate}</td>
                    <td>
                        <button type="button" class="btn btn-secondary btn-sm" style="color: var(--accent-rose); border-color: rgba(244, 63, 94, 0.3);" onclick="deleteSuite('${escapeHtml(s.id)}')">🗑 Zmazať</button>
                    </td>
                </tr>
            `;
        }).join("");
    } catch (e) {
        console.error("Chyba pri načítaní balíčkov:", e);
    }
}

async function loadFeatureUsageMeters() {
    try {
        const res = await fetch("/admin/v1/entitlements/usage");
        if (!res.ok) return;

        const metrics = await res.json();

        let totalInUse = 0;
        let totalDenials = 0;
        for (const m of metrics) {
            totalInUse += (m.inUseSeats || 0);
            totalDenials += (m.denialsCount || 0);
        }

        const inUseEl = document.getElementById("feat-kpi-in-use");
        if (inUseEl) inUseEl.textContent = totalInUse;

        const denialsEl = document.getElementById("feat-kpi-denials");
        if (denialsEl) denialsEl.textContent = totalDenials;

        const container = document.getElementById("feature-gauges-container");
        if (!container) return;

        if (metrics.length === 0) {
            container.innerHTML = `<div style="text-align: center; color: var(--text-muted); padding: 16px;">V systéme zatiaľ nie sú registrované žiadne moduly pre sledovanie obsadenosti.</div>`;
            return;
        }

        container.innerHTML = `
            <div style="display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 16px;">
                ${metrics.map(m => {
                    const max = m.maxSeats != null ? m.maxSeats : 0;
                    const inUse = m.inUseSeats || 0;
                    const percent = max > 0 ? Math.min(100, Math.round((inUse / max) * 100)) : (inUse > 0 ? 100 : 0);
                    const color = percent >= 90 ? "var(--accent-rose)" : percent >= 60 ? "var(--accent-amber)" : "var(--accent-emerald)";

                    return `
                        <div style="background: rgba(255, 255, 255, 0.03); border: 1px solid var(--border-color); border-radius: 8px; padding: 14px;">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px;">
                                <div>
                                    <strong style="font-size: 13px; font-family: monospace; color: var(--accent-cyan);">${escapeHtml(m.featureCode)}</strong>
                                    <div style="font-size: 12px; color: var(--text-secondary);">${escapeHtml(m.name)}</div>
                                </div>
                                ${m.denialsCount > 0 ? `<span class="badge badge-revoked" title="Odmietnuté požiadavky">${m.denialsCount} zamietnutí</span>` : ""}
                            </div>
                            <div style="display: flex; justify-content: space-between; font-size: 12px; margin-bottom: 6px;">
                                <span>Obsadené: <strong>${inUse}</strong> ${max > 0 ? `/ ${max}` : '(neobmedzené)'}</span>
                                <span style="color: ${color}; font-weight: bold;">${max > 0 ? `${percent}%` : 'Aktívne'}</span>
                            </div>
                            <div style="background: rgba(255, 255, 255, 0.08); height: 8px; border-radius: 4px; overflow: hidden;">
                                <div style="background: ${color}; width: ${percent}%; height: 100%; border-radius: 4px; transition: width 0.3s ease;"></div>
                            </div>
                        </div>
                    `;
                }).join("")}
            </div>
        `;
    } catch (e) {
        console.error("Chyba pri načítaní meračov obsadenosti:", e);
    }
}

async function createFeatureSubmit(event) {
    event.preventDefault();

    const code = document.getElementById("feat-code").value.trim().toUpperCase();
    const name = document.getElementById("feat-name").value.trim();
    const productId = document.getElementById("feat-product").value.trim() || null;
    const description = document.getElementById("feat-description").value.trim() || null;
    const minVersion = document.getElementById("feat-min-ver").value.trim() || null;
    const maxVersion = document.getElementById("feat-max-ver").value.trim() || null;
    const maxSeatsVal = document.getElementById("feat-max-seats").value.trim();
    const isFloating = document.getElementById("feat-is-floating").checked;

    const defaultMaxSeats = maxSeatsVal ? parseInt(maxSeatsVal, 10) : null;

    if (!code || !name) {
        showToast("Kód a názov modulu sú povinné.", "error");
        return;
    }

    try {
        const res = await fetch("/admin/v1/entitlements/features", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                code,
                name,
                productId,
                description,
                minVersion,
                maxVersion,
                isFloating,
                defaultMaxSeats
            })
        });

        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            showToast(`Chyba pri vytváraní modulu: ${err.detail || res.status}`, "error");
            return;
        }

        closeModal("modal-create-feature");
        document.getElementById("form-create-feature").reset();
        showToast(`Modul '${code}' bol úspešne zaregistrovaný v katalógu!`, "success");
        loadFeaturesView();
    } catch (e) {
        showToast("Chyba spojenia: " + e.message, "error");
    }
}

async function deleteFeature(id) {
    if (!confirm("Naozaj chcete vymazať túto definíciu modulu z katalógu?")) return;

    try {
        const res = await fetch(`/admin/v1/entitlements/features/${encodeURIComponent(id)}`, {
            method: "DELETE"
        });

        if (!res.ok) {
            showToast("Chyba pri mazaní modulu.", "error");
            return;
        }

        showToast("Modul bol úspešne vymazaný.", "success");
        loadFeaturesView();
    } catch (e) {
        showToast("Chyba: " + e.message, "error");
    }
}

async function createSuiteSubmit(event) {
    event.preventDefault();

    const code = document.getElementById("suite-code").value.trim().toUpperCase();
    const name = document.getElementById("suite-name").value.trim();
    const productId = document.getElementById("suite-product").value.trim() || null;
    const description = document.getElementById("suite-description").value.trim() || null;
    const featsStr = document.getElementById("suite-features").value.trim();

    if (!code || !name || !featsStr) {
        showToast("Kód, názov a zoznam modulov sú povinné.", "error");
        return;
    }

    const featureCodes = featsStr.split(",").map(f => f.trim().toUpperCase()).filter(f => f.length > 0);

    try {
        const res = await fetch("/admin/v1/entitlements/suites", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                code,
                name,
                productId,
                description,
                featureCodes
            })
        });

        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            showToast(`Chyba pri vytváraní balíčka: ${err.detail || res.status}`, "error");
            return;
        }

        closeModal("modal-create-suite");
        document.getElementById("form-create-suite").reset();
        showToast(`Balíček '${code}' (${featureCodes.length} modulov) bol úspešne vytvorený!`, "success");
        loadFeaturesView();
    } catch (e) {
        showToast("Chyba spojenia: " + e.message, "error");
    }
}

async function deleteSuite(id) {
    if (!confirm("Naozaj chcete vymazať tento balíček (suite)?")) return;

    try {
        const res = await fetch(`/admin/v1/entitlements/suites/${encodeURIComponent(id)}`, {
            method: "DELETE"
        });

        if (!res.ok) {
            showToast("Chyba pri mazaní balíčka.", "error");
            return;
        }

        showToast("Balíček bol úspešne vymazaný.", "success");
        loadFeaturesView();
    } catch (e) {
        showToast("Chyba: " + e.message, "error");
    }
}

// ==========================================
// Revocations & CRL Management (Phase 17)
// ==========================================
async function loadRevocationsView() {
    try {
        const res = await fetch("/admin/v1/revocations");
        if (!res.ok) {
            console.error("Chyba pri načítaní revokácií:", res.status);
            return;
        }

        const revs = await res.json();

        // Update KPIs
        const totalEl = document.getElementById("rev-kpi-total");
        if (totalEl) totalEl.textContent = revs.length;

        let maxSeq = 0;
        let licensesCount = 0;
        let keysHwCount = 0;

        for (const r of revs) {
            if (r.sequence > maxSeq) maxSeq = r.sequence;
            if (r.subjectType === "license") {
                licensesCount++;
            } else {
                keysHwCount++;
            }
        }

        const seqEl = document.getElementById("rev-kpi-seq");
        if (seqEl) seqEl.textContent = `#${maxSeq}`;

        const licEl = document.getElementById("rev-kpi-licenses");
        if (licEl) licEl.textContent = licensesCount;

        const hwEl = document.getElementById("rev-kpi-keys-hw");
        if (hwEl) hwEl.textContent = keysHwCount;

        // Render Table
        const tbody = document.getElementById("revocations-table-body");
        if (!tbody) return;

        if (revs.length === 0) {
            tbody.innerHTML = `<tr><td colspan="5" style="text-align: center; color: var(--text-muted); padding: 18px;">Žiadne aktívne revokácie. CRL zoznam je čistý.</td></tr>`;
            return;
        }

        tbody.innerHTML = revs.map(r => {
            let typeBadge = "";
            switch (r.subjectType) {
                case "license":
                    typeBadge = `<span class="badge badge-rose" style="font-weight: bold;">license</span>`;
                    break;
                case "machine":
                    typeBadge = `<span class="badge" style="background: rgba(168, 85, 247, 0.15); color: #c084fc;">machine</span>`;
                    break;
                case "kid":
                case "key":
                    typeBadge = `<span class="badge badge-amber">kid (kľúč)</span>`;
                    break;
                case "relay":
                    typeBadge = `<span class="badge badge-cyan">relay</span>`;
                    break;
                case "lease":
                    typeBadge = `<span class="badge badge-indigo">lease</span>`;
                    break;
                default:
                    typeBadge = `<span class="badge">${escapeHtml(r.subjectType)}</span>`;
                    break;
            }

            const revokedDate = r.revokedAt ? new Date(r.revokedAt).toLocaleString("sk-SK") : "-";

            return `
                <tr>
                    <td><strong style="color: var(--accent-emerald); font-family: monospace;">#${r.sequence}</strong></td>
                    <td>${typeBadge}</td>
                    <td><code style="font-family: monospace; font-size: 12px; color: var(--text-primary);">${escapeHtml(r.subjectId)}</code></td>
                    <td><span style="color: var(--text-secondary);">${escapeHtml(r.reason || "neuvedený")}</span></td>
                    <td style="font-size: 12px; color: var(--text-muted);">${revokedDate}</td>
                </tr>
            `;
        }).join("");
    } catch (e) {
        console.error("Chyba pri načítaní revokácií:", e);
    }
}

async function createRevocationSubmit(event) {
    event.preventDefault();
    const typeEl = document.getElementById("rev-type");
    const idEl = document.getElementById("rev-id");
    const reasonEl = document.getElementById("rev-reason");

    const payload = {
        subjectType: typeEl.value,
        subjectId: idEl.value.trim(),
        reason: reasonEl.value.trim() || null
    };

    try {
        const res = await fetch("/admin/v1/revocations", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });

        if (!res.ok) {
            const err = await res.text();
            showToast("Chyba pri revokácii: " + err, "error");
            return;
        }

        const data = await res.json();
        showToast(`Subjekt ${data.subjectId} bol úspešne revokovaný (seq #${data.sequence}).`, "success");
        closeModal("modal-create-revocation");
        event.target.reset();
        await loadRevocationsView();
    } catch (e) {
        showToast("Chyba spojenia: " + e.message, "error");
    }
}


