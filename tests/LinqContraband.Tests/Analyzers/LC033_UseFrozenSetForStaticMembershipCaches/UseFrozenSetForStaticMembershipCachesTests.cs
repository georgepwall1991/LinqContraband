using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches.UseFrozenSetForStaticMembershipCachesAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches;

public class UseFrozenSetForStaticMembershipCachesTests
{
    private const string Usings = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
";

    private const string FrozenSupport = @"
namespace System.Collections.Frozen
{
    using System.Collections.Generic;

    public abstract class FrozenSet<T> : IEnumerable<T>
    {
        public abstract bool Contains(T item);
        public abstract IEnumerator<T> GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class FrozenSetExtensions
    {
        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source) => null;
        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source, IEqualityComparer<T> comparer) => null;
    }
}
";

    [Fact]
    public async Task PrivateStaticReadonlyHashSet_WithDirectContains_Triggers()
    {
        var test = Usings + FrozenSupport + @"
class Program
{
    {|LC033:private static readonly HashSet<string> ElevatedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ""admin"",
        ""ops""
    };|}

    static bool IsElevated(string role) => ElevatedRoles.Contains(role);
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PrivateStaticReadonlyHashSet_UsedInEnumerableLambda_Triggers()
    {
        var test = Usings + FrozenSupport + @"
class Program
{
    {|LC033:private static readonly HashSet<int> ReservedIds = new() { 1, 2, 3 };|}

    static List<int> Filter(IEnumerable<int> values)
    {
        return values.Where(value => ReservedIds.Contains(value)).ToList();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SourceToHashSetInitializer_Triggers()
    {
        var test = Usings + FrozenSupport + @"
class Program
{
    private static readonly int[] SeedValues = { 1, 2, 3 };
    {|LC033:private static readonly HashSet<int> ReservedIds = SeedValues.ToHashSet();|}

    static bool IsReserved(int value) => ReservedIds.Contains(value);
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StaticEnumerableToHashSetInitializer_DoesNotTrigger()
    {
        var test = Usings + FrozenSupport + @"
class Program
{
    private static readonly int[] SeedValues = { 1, 2, 3 };
    private static readonly HashSet<int> ReservedIds = Enumerable.ToHashSet(SeedValues);

    static bool IsReserved(int value) => ReservedIds.Contains(value);
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonPrivateField_DoesNotTrigger()
    {
        var test = Usings + FrozenSupport + @"
class Program
{
    internal static readonly HashSet<int> ReservedIds = new() { 1, 2, 3 };

    static bool IsReserved(int value) => ReservedIds.Contains(value);
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StaticConstructorInitialization_DoesNotTrigger()
    {
        var test = Usings + FrozenSupport + @"
class Program
{
    private static readonly HashSet<int> ReservedIds;

    static Program()
    {
        ReservedIds = new HashSet<int> { 1, 2, 3 };
    }

    static bool IsReserved(int value) => ReservedIds.Contains(value);
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MutationAfterInitialization_DoesNotTrigger()
    {
        var test = Usings + FrozenSupport + @"
class Program
{
    private static readonly HashSet<int> ReservedIds = new() { 1, 2, 3 };

    static void AddValue(int value)
    {
        ReservedIds.Add(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EnumerationUsage_DoesNotTrigger()
    {
        var test = Usings + FrozenSupport + @"
class Program
{
    private static readonly HashSet<int> ReservedIds = new() { 1, 2, 3 };

    static int Sum()
    {
        var total = 0;
        foreach (var value in ReservedIds)
        {
            total += value;
        }

        return total;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AliasedUsage_DoesNotTrigger()
    {
        var test = Usings + FrozenSupport + @"
class Program
{
    private static readonly HashSet<int> ReservedIds = new() { 1, 2, 3 };

    static bool IsReserved(int value)
    {
        var cache = ReservedIds;
        return cache.Contains(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task QueryableExpressionTreeUsage_DoesNotTrigger()
    {
        var test = Usings + FrozenSupport + @"
class Program
{
    private static readonly HashSet<int> ReservedIds = new() { 1, 2, 3 };

    static List<int> Filter(IQueryable<int> values)
    {
        return values.Where(value => ReservedIds.Contains(value)).ToList();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MissingFrozenSetSupport_DoesNotTrigger()
    {
        var test = Usings + @"
class Program
{
    private static readonly HashSet<int> ReservedIds = new() { 1, 2, 3 };

    static bool IsReserved(int value) => ReservedIds.Contains(value);
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
