namespace LessonDisplay.Server.Licensing;

/// <summary>
/// The PUBLIC half of the seller's signing key — safe to ship inside the
/// app. This is what lets the app recognize a genuine license key with no
/// server and no internet connection: only whoever holds the matching
/// PRIVATE key (kept offline, never committed to source control) can
/// produce a key that verifies against this.
///
/// >>> REPLACE THIS before selling anything. <<<
/// On a machine you trust, run (from tools/LicenseTool):
///   dotnet run -- genkey --out-dir .
/// That writes private.key (keep it secret, back it up, NEVER commit it)
/// and public.key. Paste public.key's contents in place of the PEM block
/// below, rebuild, and every key you issue with your private.key will be
/// recognized.
///
/// The key below is a DEMO keypair generated for this project, with its
/// matching private key sitting right next to it in demo-keys/private.key.
/// It exists purely so licensing can be tested out of the box — anyone can
/// forge a "valid" license against it, so treat every key issued with the
/// demo private key as worthless for real sales, and swap this out first.
/// </summary>
public static class EmbeddedPublicKey
{
    public const string Pem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEydHk+V+AXu83pehRXabpUZ7yTU4z
        5l1du6MNsTNQK77+FBSeC+DN95cAv/ILheD9cpOXMvEHymLkml0bwKdk7A==
        -----END PUBLIC KEY-----
        """;
}
