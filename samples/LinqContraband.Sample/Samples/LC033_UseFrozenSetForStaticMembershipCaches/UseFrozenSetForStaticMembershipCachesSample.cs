using System.Collections.Frozen;

namespace System.Collections.Frozen
{
    internal static class SampleFrozenSetSupport
    {
        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source) => throw new NotSupportedException();

        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source, IEqualityComparer<T> comparer) =>
            throw new NotSupportedException();
    }
}

namespace LinqContraband.Sample.Samples.LC033_UseFrozenSetForStaticMembershipCaches
{
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
}
