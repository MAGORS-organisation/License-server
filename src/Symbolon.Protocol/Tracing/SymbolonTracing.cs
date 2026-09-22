using System.Diagnostics;

namespace Symbolon.Protocol.Tracing;

/// <summary>
/// Centrálny zdroj distribuovaného trasovania pre platformu Symbolon.
/// Plne kompatibilný s OpenTelemetry a W3C TraceContext štandardmi.
/// </summary>
public static class SymbolonTracing
{
    public const string ActivitySourceName = "Symbolon";
    public const string ActivitySourceVersion = "1.0.0";

    /// <summary>
    /// Globálna inštancia ActivitySource pre Symbolon aplikácie a komponenty.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, ActivitySourceVersion);

    // Štandardizované názvy operácií (Span Names)
    public const string OpCheckout = "symbolon.checkout";
    public const string OpRenew = "symbolon.renew";
    public const string OpRelease = "symbolon.release";
    public const string OpBorrow = "symbolon.borrow";
    public const string OpReturnBorrowed = "symbolon.return_borrowed";
    public const string OpFraudCheck = "symbolon.fraud_check";
    public const string OpReplicationSync = "symbolon.replication.sync";
    public const string OpEbpfEnforce = "symbolon.ebpf.enforce";
    public const string OpSecretSplit = "symbolon.keys.split";
    public const string OpSecretCombine = "symbolon.keys.combine";

    // Štandardizované tagy / atribúty
    public const string TagLicenseId = "symbolon.license_id";
    public const string TagLeaseId = "symbolon.lease_id";
    public const string TagClientId = "symbolon.client_id";
    public const string TagSeatCount = "symbolon.seat_count";
    public const string TagBorrowDays = "symbolon.borrow_days";
    public const string TagFraudRisk = "symbolon.fraud_risk";
    public const string TagVelocityKmh = "symbolon.velocity_kmh";
    public const string TagVmCloning = "symbolon.vm_cloning";
    public const string TagPeerNode = "symbolon.peer_node";
    public const string TagAlgorithm = "symbolon.crypto_algorithm";
    public const string TagThresholdK = "symbolon.sss.threshold_k";
    public const string TagTotalSharesN = "symbolon.sss.total_n";
}
