using System.Security.Cryptography;
using LessonDisplay.Licensing;

namespace LessonDisplay.Server.Licensing;

public sealed record LicenseStatus(string State, bool Usable, int DaysRemaining, LicensePayload? Payload);

/// <summary>
/// Tracks whether this install is licensed, mid-trial, or past its trial —
/// entirely from local files, no network calls. A fresh install gets a
/// 7-day trial starting the first time this ever runs; entering a valid
/// license key (any time, including before the trial starts) unlocks the
/// app permanently regardless of the trial clock.
/// </summary>
public sealed class LicenseState
{
    private const int TrialDays = 7;

    private readonly string _licenseDir;
    private readonly ECDsa _publicKey;
    private readonly object _lock = new();

    public LicenseState(string licenseDir, string publicKeyPem)
    {
        _licenseDir = licenseDir;
        Directory.CreateDirectory(_licenseDir);
        _publicKey = ECDsa.Create();
        _publicKey.ImportFromPem(publicKeyPem);
        EnsureTrialStarted();
    }

    private string LicenseFilePath => Path.Combine(_licenseDir, "license.key");
    private string TrialMarkerPath => Path.Combine(_licenseDir, "trial-started.txt");

    private void EnsureTrialStarted()
    {
        lock (_lock)
        {
            if (!File.Exists(TrialMarkerPath))
            {
                File.WriteAllText(TrialMarkerPath, DateTimeOffset.UtcNow.ToString("O"));
            }
        }
    }

    public LicenseStatus GetStatus()
    {
        lock (_lock)
        {
            if (File.Exists(LicenseFilePath))
            {
                var storedKey = File.ReadAllText(LicenseFilePath).Trim();
                if (LicenseCodec.TryVerify(storedKey, _publicKey, out var payload, out _) && payload is not null)
                {
                    if (!payload.IsExpired(DateTimeOffset.UtcNow))
                    {
                        return new LicenseStatus("licensed", true, int.MaxValue, payload);
                    }
                    // Stored key is authentic but has lapsed (a subscription that
                    // wasn't renewed) — fall through and treat like unlicensed.
                }
            }

            var trialStart = File.Exists(TrialMarkerPath) && DateTimeOffset.TryParse(File.ReadAllText(TrialMarkerPath), out var t)
                ? t
                : DateTimeOffset.UtcNow;
            var daysElapsed = (DateTimeOffset.UtcNow - trialStart).TotalDays;
            var daysRemaining = Math.Max(0, TrialDays - (int)Math.Floor(daysElapsed));

            return daysElapsed < TrialDays
                ? new LicenseStatus("trial", true, daysRemaining, null)
                : new LicenseStatus("expired", false, 0, null);
        }
    }

    public (bool ok, string? error) Activate(string licenseKey)
    {
        if (!LicenseCodec.TryVerify(licenseKey, _publicKey, out var payload, out var error) || payload is null)
        {
            return (false, error ?? "Invalid license key.");
        }
        if (payload.IsExpired(DateTimeOffset.UtcNow))
        {
            return (false, "This license key has expired. Contact support for a renewal.");
        }

        lock (_lock)
        {
            File.WriteAllText(LicenseFilePath, licenseKey.Trim());
        }
        return (true, null);
    }
}
