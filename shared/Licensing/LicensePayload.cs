using System.Text.Json;
using System.Text.Json.Serialization;

namespace LessonDisplay.Licensing;

/// <summary>
/// What a license key actually says, once verified. Kept intentionally
/// small — this is the whole "contract" a license makes with the app.
/// </summary>
public sealed class LicensePayload
{
    /// <summary>Short random id for this specific license, for support/lookup purposes.</summary>
    public string Id { get; set; } = "";

    /// <summary>Buyer name and/or email, whatever was on the sale.</summary>
    public string Licensee { get; set; } = "";

    /// <summary>Always "ClassSync" for now — lets one key format cover future products.</summary>
    public string Product { get; set; } = "ClassSync";

    /// <summary>"perpetual" (buy once, use forever) or "subscription" (must be renewed by Expires).</summary>
    public string PlanType { get; set; } = "perpetual";

    public DateTimeOffset IssuedUtc { get; set; }

    /// <summary>Null for a perpetual license. Set for a subscription license.</summary>
    public DateTimeOffset? ExpiresUtc { get; set; }

    public bool IsExpired(DateTimeOffset now) => ExpiresUtc is not null && now > ExpiresUtc.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static LicensePayload FromJson(string json) =>
        JsonSerializer.Deserialize<LicensePayload>(json)
        ?? throw new InvalidOperationException("Empty license payload");
}
