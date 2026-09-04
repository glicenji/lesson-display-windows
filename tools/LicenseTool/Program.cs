using System.Security.Cryptography;
using LessonDisplay.Licensing;

// This tool is for YOU, the seller — never distribute it with the app, and
// never commit private.key to source control (it's what lets anyone mint
// valid license keys; if it leaks, generate a new keypair and every key
// issued under the old one keeps working until you also update the public
// key baked into the server — so treat it like a password).

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

switch (args[0])
{
    case "genkey":
        return GenKey(args[1..]);
    case "issue":
        return Issue(args[1..]);
    case "verify":
        return Verify(args[1..]);
    default:
        PrintUsage();
        return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        LicenseTool — generate and issue ClassSync license keys.

        Usage:
          LicenseTool genkey [--out-dir <dir>]
              Creates private.key and public.key (PEM files) in <dir>
              (default: current directory). Run this ONCE, ever, per
              product line. Keep private.key secret and safe — back it up
              somewhere private (a password manager, an encrypted drive).
              Paste the printed public key into
              src/LessonDisplay.Server/Licensing/EmbeddedPublicKey.cs so the
              app can recognize keys you issue.

          LicenseTool issue --private <path> --name "<buyer>" [--email <email>]
                             [--type perpetual|subscription] [--years <n>]
              Mints one license key using your private key. Prints the key
              — copy/paste (or email) it to the buyer. Default --type is
              perpetual (no expiry); pass --type subscription --years 1 for
              a renewing license.

          LicenseTool verify --public <path> --key <licensekey>
              Checks a key the way the app would, and prints what it says
              (buyer, plan, issued/expiry dates). Useful for support
              questions ("is my key still valid").
        """);
}

static int GenKey(string[] args)
{
    var outDir = GetOption(args, "--out-dir") ?? Directory.GetCurrentDirectory();
    Directory.CreateDirectory(outDir);
    var privatePath = Path.Combine(outDir, "private.key");
    var publicPath = Path.Combine(outDir, "public.key");

    if (File.Exists(privatePath))
    {
        Console.Error.WriteLine($"Refusing to overwrite existing {privatePath}.");
        Console.Error.WriteLine("If you really want a new keypair, move or delete it first —");
        Console.Error.WriteLine("but note every key you've already issued was signed with the OLD one.");
        return 1;
    }

    using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var privatePem = ecdsa.ExportECPrivateKeyPem();
    var publicPem = ecdsa.ExportSubjectPublicKeyInfoPem();

    File.WriteAllText(privatePath, privatePem);
    File.WriteAllText(publicPath, publicPem);

    Console.WriteLine($"Wrote {privatePath}  (SECRET — back this up somewhere private, never commit it)");
    Console.WriteLine($"Wrote {publicPath}  (safe to embed in the app)");
    Console.WriteLine();
    Console.WriteLine("Public key (paste this into EmbeddedPublicKey.cs):");
    Console.WriteLine();
    Console.WriteLine(publicPem);
    return 0;
}

static int Issue(string[] args)
{
    var privatePath = GetOption(args, "--private");
    var name = GetOption(args, "--name");
    var email = GetOption(args, "--email");
    var type = GetOption(args, "--type") ?? "perpetual";
    var yearsStr = GetOption(args, "--years");

    if (privatePath is null || name is null)
    {
        Console.Error.WriteLine("Usage: LicenseTool issue --private <path> --name \"<buyer>\" [--email <email>] [--type perpetual|subscription] [--years <n>]");
        return 1;
    }
    if (type != "perpetual" && type != "subscription")
    {
        Console.Error.WriteLine("--type must be 'perpetual' or 'subscription'");
        return 1;
    }

    using var ecdsa = ECDsa.Create();
    ecdsa.ImportFromPem(File.ReadAllText(privatePath));

    var now = DateTimeOffset.UtcNow;
    DateTimeOffset? expires = null;
    if (type == "subscription")
    {
        var years = int.TryParse(yearsStr, out var y) ? y : 1;
        expires = now.AddYears(years);
    }

    var payload = new LicensePayload
    {
        Id = Guid.NewGuid().ToString("N")[..10],
        Licensee = email is null ? name : $"{name} <{email}>",
        PlanType = type,
        IssuedUtc = now,
        ExpiresUtc = expires,
    };

    var key = LicenseCodec.Issue(payload, ecdsa);

    Console.WriteLine($"License id:  {payload.Id}");
    Console.WriteLine($"Licensee:    {payload.Licensee}");
    Console.WriteLine($"Plan:        {payload.PlanType}{(expires is not null ? $" (expires {expires:yyyy-MM-dd})" : " (no expiry)")}");
    Console.WriteLine();
    Console.WriteLine("License key (send this to the buyer):");
    Console.WriteLine();
    Console.WriteLine(key);
    return 0;
}

static int Verify(string[] args)
{
    var publicPath = GetOption(args, "--public");
    var key = GetOption(args, "--key");
    if (publicPath is null || key is null)
    {
        Console.Error.WriteLine("Usage: LicenseTool verify --public <path> --key <licensekey>");
        return 1;
    }

    using var ecdsa = ECDsa.Create();
    ecdsa.ImportFromPem(File.ReadAllText(publicPath));

    if (!LicenseCodec.TryVerify(key, ecdsa, out var payload, out var error))
    {
        Console.WriteLine($"INVALID: {error}");
        return 1;
    }

    Console.WriteLine("VALID signature.");
    Console.WriteLine($"License id:  {payload!.Id}");
    Console.WriteLine($"Licensee:    {payload.Licensee}");
    Console.WriteLine($"Product:     {payload.Product}");
    Console.WriteLine($"Plan:        {payload.PlanType}");
    Console.WriteLine($"Issued:      {payload.IssuedUtc:yyyy-MM-dd}");
    Console.WriteLine($"Expires:     {(payload.ExpiresUtc is null ? "never" : payload.ExpiresUtc.Value.ToString("yyyy-MM-dd"))}");
    Console.WriteLine($"Expired now: {payload.IsExpired(DateTimeOffset.UtcNow)}");
    return 0;
}

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == name) return args[i + 1];
    }
    return null;
}
