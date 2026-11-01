using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC004_IQueryableLeak.IQueryableLeakAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC004_IQueryableLeak;

/// <summary>
/// Leftover LC004 <c>HazardousEnumerableMethods</c> and
/// <c>MaterializingCollectionTypes</c> names the original 12 analyzer cases do
/// not isolate. Dropping <c>MaxBy</c> from the method set FNs only
/// <see cref="MaxBy_Reports"/>; the shipped <c>Any</c>/<c>Count</c> fixtures
/// stay green. Dropping <c>HashSet</c> from the constructor set FNs only
/// <see cref="HashSetConstructor_Reports"/>; the shipped <c>List</c>
/// constructor fixture stays green. <c>MaxBy</c>/<c>MinBy</c> run on Net80
/// because the default VerifyCS references do not declare those .NET 6
/// methods. <c>EnumerableToList_Reports</c> uses the unreduced static spelling
/// so <c>GetInvocationReceiver</c>'s <c>Arguments[0]</c> fallback is not locked
/// only by extension syntax.
/// </summary>
public sealed class IQueryableLeakCoverageTests
{
    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
using TestNamespace;
";

    private const string MockNamespace = @"
namespace TestNamespace
{
    public class User
    {
        public int Id { get; set; }
    }

    public class DbContext : IDisposable
    {
        public void Dispose() { }
    }

    public class DbSet<T> : IQueryable<T>
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }
}
";

    private static string BuildSource(string calleeBody)
    {
        return Usings + @"
namespace TestApp
{
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
    }

    public sealed class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var query = db.Users.Where(u => u.Id > 10);
            Consume({|LC004:query|});
        }

        private static void Consume(IEnumerable<User> users)
        {
            " + calleeBody + @"
        }
    }
}
" + MockNamespace;
    }

    private static Task VerifyLeakAsync(string calleeBody) =>
        VerifyCS.VerifyAnalyzerAsync(BuildSource(calleeBody));

    // MaxBy/MinBy are .NET 6 Enumerable methods. The default VerifyCS
    // reference assemblies do not declare them, so those shapes compile as
    // errors (MissingMethodException = diagnostic/compile mismatch) and cannot
    // isolate the name unless we raise the harness to Net80.
    private static Task VerifyLeakOnNet80Async(string calleeBody)
    {
        var test = new CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC004_IQueryableLeak.IQueryableLeakAnalyzer,
            XUnitVerifier>
        {
            TestCode = BuildSource(calleeBody),
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        return test.RunAsync();
    }

    [Fact]
    public Task Aggregate_Reports() =>
        VerifyLeakAsync("_ = users.Aggregate(0, (acc, user) => acc + user.Id);");

    [Fact]
    public Task All_Reports() =>
        VerifyLeakAsync("_ = users.All(user => user.Id > 0);");

    [Fact]
    public Task Average_Reports() =>
        VerifyLeakAsync("_ = users.Average(user => user.Id);");

    [Fact]
    public Task Contains_Reports() =>
        VerifyLeakAsync("_ = users.Contains(new User());");

    [Fact]
    public Task ElementAt_Reports() =>
        VerifyLeakAsync("_ = users.ElementAt(0);");

    [Fact]
    public Task ElementAtOrDefault_Reports() =>
        VerifyLeakAsync("_ = users.ElementAtOrDefault(0);");

    [Fact]
    public Task First_Reports() =>
        VerifyLeakAsync("_ = users.First();");

    [Fact]
    public Task FirstOrDefault_Reports() =>
        VerifyLeakAsync("_ = users.FirstOrDefault();");

    [Fact]
    public Task Last_Reports() =>
        VerifyLeakAsync("_ = users.Last();");

    [Fact]
    public Task LastOrDefault_Reports() =>
        VerifyLeakAsync("_ = users.LastOrDefault();");

    [Fact]
    public Task LongCount_Reports() =>
        VerifyLeakAsync("_ = users.LongCount();");

    [Fact]
    public Task Max_Reports() =>
        VerifyLeakAsync("_ = users.Max(user => user.Id);");

    [Fact]
    public Task MaxBy_Reports() =>
        VerifyLeakOnNet80Async("_ = users.MaxBy(user => user.Id);");

    [Fact]
    public Task Min_Reports() =>
        VerifyLeakAsync("_ = users.Min(user => user.Id);");

    [Fact]
    public Task MinBy_Reports() =>
        VerifyLeakOnNet80Async("_ = users.MinBy(user => user.Id);");

    [Fact]
    public Task SequenceEqual_Reports() =>
        VerifyLeakAsync("_ = users.SequenceEqual(Array.Empty<User>());");

    [Fact]
    public Task Single_Reports() =>
        VerifyLeakAsync("_ = users.Single();");

    [Fact]
    public Task SingleOrDefault_Reports() =>
        VerifyLeakAsync("_ = users.SingleOrDefault();");

    [Fact]
    public Task Sum_Reports() =>
        VerifyLeakAsync("_ = users.Sum(user => user.Id);");

    [Fact]
    public Task ToArray_Reports() =>
        VerifyLeakAsync("_ = users.ToArray();");

    [Fact]
    public Task ToDictionary_Reports() =>
        VerifyLeakAsync("_ = users.ToDictionary(user => user.Id);");

    [Fact]
    public Task ToHashSet_Reports() =>
        VerifyLeakAsync("_ = users.ToHashSet();");

    [Fact]
    public Task ToList_Reports() =>
        VerifyLeakAsync("_ = users.ToList();");

    [Fact]
    public Task EnumerableToList_Reports() =>
        VerifyLeakAsync("_ = Enumerable.ToList(users);");

    [Fact]
    public Task ToLookup_Reports() =>
        VerifyLeakAsync("_ = users.ToLookup(user => user.Id);");

    [Fact]
    public Task HashSetConstructor_Reports() =>
        VerifyLeakAsync("_ = new HashSet<User>(users);");

    [Fact]
    public Task LinkedListConstructor_Reports() =>
        VerifyLeakAsync("_ = new LinkedList<User>(users);");

    [Fact]
    public Task QueueConstructor_Reports() =>
        VerifyLeakAsync("_ = new Queue<User>(users);");

    [Fact]
    public Task SortedSetConstructor_Reports() =>
        VerifyLeakAsync("_ = new SortedSet<User>(users, Comparer<User>.Create((left, right) => left.Id.CompareTo(right.Id)));");

    [Fact]
    public Task StackConstructor_Reports() =>
        VerifyLeakAsync("_ = new Stack<User>(users);");
}
