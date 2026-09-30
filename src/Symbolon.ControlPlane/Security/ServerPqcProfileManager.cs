using Symbolon.Crypto;

namespace Symbolon.ControlPlane.Security;

/// <summary>
/// Manages runtime cryptographic profile state for the Symbolon Control Plane.
/// Supports switching between 'hybrid-v1' and 'pqc-strict' (Zero Classical Cryptography).
/// </summary>
public static class ServerPqcProfileManager
{
    private static volatile string _activeProfile = PqcProfiles.HybridV1;

    public static string ActiveProfile
    {
        get => _activeProfile;
        set
        {
            if (!PqcProfiles.IsValidProfile(value))
            {
                throw new ArgumentException(
                    $"Invalid PQC profile: '{value}'. Supported profiles are '{PqcProfiles.HybridV1}' and '{PqcProfiles.PqcStrict}'.",
                    nameof(value));
            }
            _activeProfile = value;
        }
    }
}
