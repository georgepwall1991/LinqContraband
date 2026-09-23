namespace LinqContraband.Sample.Samples.LC033_UseFrozenSetForStaticMembershipCaches;

// No FrozenSet shim: LC033 finds ToFrozenSet in the net8.0+ framework metadata.
public sealed class UseFrozenSetForStaticMembershipCachesSample
{
    private static readonly HashSet<string> ElevatedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin",
        "ops"
    };

    public static void Run()
    {
        Console.WriteLine("Testing LC033...");

        // ADVISORY: This cache is read-only and used only for membership checks.
        Console.WriteLine(IsElevated("admin"));
    }

    private static bool IsElevated(string role) => ElevatedRoles.Contains(role);
}
