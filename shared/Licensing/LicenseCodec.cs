using System.Security.Cryptography;
using System.Text;

namespace LessonDisplay.Licensing;

/// <summary>
/// Turns a LicensePayload into the string a customer pastes in as their
/// license key, and back. Format: "CSN1." + base64url(payload JSON) + "." +
/// base64url(ECDSA P-256 / SHA-256 signature over the JSON bytes).
///
/// This never needs a server to check in with — the app only ever needs the
/// PUBLIC key (safe to ship inside it). Only whoever holds the PRIVATE key
/// (kept offline, never committed to source control) can produce a key that
/// verifies successfully, so a customer — or anyone reading the app's
/// source — cannot forge one.
/// </summary>
public static class LicenseCodec
{
    private const string Prefix = "CSN1";

    public static string Issue(LicensePayload payload, ECDsa privateKey)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payload.ToJson());
        var signature = privateKey.SignData(payloadBytes, HashAlgorithmName.SHA256);
        return $"{Prefix}.{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";
    }

    /// <summary>
    /// Verifies the signature and returns the payload if (and only if) the
    /// key is authentic. Does NOT check expiry — call payload.IsExpired
    /// yourself, since "authentic but expired" and "not authentic at all"
    /// are different situations worth telling the user apart.
    /// </summary>
    public static bool TryVerify(string licenseKey, ECDsa publicKey, out LicensePayload? payload, out string? error)
    {
        payload = null;
        error = null;
        try
        {
            var trimmed = (licenseKey ?? "").Trim();
            var parts = trimmed.Split('.');
            if (parts.Length != 3 || parts[0] != Prefix)
            {
                error = "That doesn't look like a ClassSync license key.";
                return false;
            }

            var payloadBytes = Base64UrlDecode(parts[1]);
            var signature = Base64UrlDecode(parts[2]);

            if (!publicKey.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256))
            {
                error = "This license key's signature doesn't check out — it may be mistyped or not genuine.";
                return false;
            }

            payload = LicensePayload.FromJson(Encoding.UTF8.GetString(payloadBytes));
            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not read this license key: {ex.Message}";
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
