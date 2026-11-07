using AutoCorrect.Core.Licensing;

// Usage:
//   dotnet run --project tools/LicenseKeyGen -- email@example.com personal
//   dotnet run --project tools/LicenseKeyGen -- email@example.com lifetime
//   dotnet run --project tools/LicenseKeyGen -- email@example.com personal 2027-10-31
//
// After a PayPal payment, run this and email/paste the key into Settings → License.

if (args.Length < 2)
{
    Console.WriteLine("LicenseKeyGen — create an AutoCorrect offline license key");
    Console.WriteLine();
    Console.WriteLine("  LicenseKeyGen <email> <tier> [yyyy-MM-dd expiry]");
    Console.WriteLine();
    Console.WriteLine("Tiers: personal | pro | team | lifetime");
    Console.WriteLine("Omit expiry (or use lifetime tier) for never-expires.");
    Console.WriteLine("Default personal expiry if date omitted: +365 days.");
    return 1;
}

var email = args[0].Trim();
var tierRaw = args[1].Trim().ToLowerInvariant();
LicenseTier tier = tierRaw switch
{
    "personal" => LicenseTier.Personal,
    "pro" => LicenseTier.Pro,
    "team" => LicenseTier.Team,
    "lifetime" => LicenseTier.Lifetime,
    _ => LicenseTier.None
};

if (tier == LicenseTier.None)
{
    Console.Error.WriteLine("Unknown tier. Use personal, pro, team, or lifetime.");
    return 2;
}

DateTime? exp = null;
if (tier != LicenseTier.Lifetime)
{
    if (args.Length >= 3 && DateTime.TryParse(args[2], out var parsed))
        exp = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
    else
        exp = DateTime.UtcNow.Date.AddDays(365);
}

var key = LicenseService.GenerateKey(email, tier, exp);
Console.WriteLine(key);
Console.WriteLine();
Console.WriteLine($"email={email} tier={tierRaw} exp={(exp is null ? "never" : exp.Value.ToString("yyyy-MM-dd"))}");
return 0;
