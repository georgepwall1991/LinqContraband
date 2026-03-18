namespace LinqContraband.Sample.Samples.LC033_UseFrozenSetForStaticMembershipCaches;

public static class UseFrozenSetForStaticMembershipCachesSample
{
    private static readonly HashSet<string> ElevatedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin",
        "ops"
    };

    public static void Run()
    {
        Console.WriteLine("Testing LC033...");

        var roles = new List<string> { "admin", "guest" };

        // ADVISORY: This cache is read-only and used only for membership checks.
        var elevatedRoles = roles.Where(role => ElevatedRoles.Contains(role)).ToList();
        Console.WriteLine(elevatedRoles.Count);
    }
}
