using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC013_DisposedContextQuery.DisposedContextQueryAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC013_DisposedContextQuery;

/// <summary>
/// Leftover LC013 <c>IsMaterializingLinqOperator</c> names the original
/// <c>ToList().AsQueryable()</c> fixture does not isolate. Dropping
/// <c>ToArray</c> FNs only <see cref="ToArray_ThenAsQueryable_StaysQuiet"/>;
/// the shipped <c>ToList</c> fixture stays green. Dropping <c>ToHashSet</c>
/// treats <c>ToHashSet().AsQueryable()</c> as a transparent query chain and
/// reports a disposed-context leak even though enumeration no longer depends
/// on the context. <c>ToHashSet</c> runs on Net80 because the default VerifyCS
/// references do not declare that .NET Standard 2.1 Enumerable method.
/// Do not add a second <c>ToList</c> pin.
/// </summary>
public sealed class DisposedContextQueryCoverageTests
{
    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TestNamespace;
";

    private const string MockNamespace = @"
namespace TestNamespace
{
    public class User { public int Id { get; set; } }
}

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable, IAsyncDisposable
    {
        public void Dispose() {}
        public ValueTask DisposeAsync() => default;
        public DbSet<T> Set<T>() where T : class => new DbSet<T>();
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

    private static string BuildReturn(string returnExpression)
    {
        return Usings + @"
class Program
{
    public IQueryable GetUsers()
    {
        using var db = new DbContext();
        return " + returnExpression + @";
    }
}" + MockNamespace;
    }

    private static Task VerifyQuietAsync(string returnExpression) =>
        VerifyCS.VerifyAnalyzerAsync(BuildReturn(returnExpression));

    // ToHashSet is a .NET Standard 2.1 Enumerable method. The default
    // VerifyCS reference assemblies do not declare it, so the shape compiles
    // as an error (MissingMethodException = diagnostic/compile mismatch) and
    // cannot isolate the name unless we raise the harness to Net80.
    private static Task VerifyQuietOnNet80Async(string returnExpression)
    {
        var test = new CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC013_DisposedContextQuery.DisposedContextQueryAnalyzer,
            XUnitVerifier>
        {
            TestCode = BuildReturn(returnExpression),
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        return test.RunAsync();
    }

    [Fact]
    public Task ToArray_ThenAsQueryable_StaysQuiet() =>
        VerifyQuietAsync("db.Set<User>().Where(u => u.Id > 1).ToArray().AsQueryable()");

    [Fact]
    public Task ToDictionary_ThenAsQueryable_StaysQuiet() =>
        VerifyQuietAsync("db.Set<User>().Where(u => u.Id > 1).ToDictionary(u => u.Id).AsQueryable()");

    [Fact]
    public Task ToHashSet_ThenAsQueryable_StaysQuiet() =>
        VerifyQuietOnNet80Async("db.Set<User>().Where(u => u.Id > 1).ToHashSet().AsQueryable()");

    [Fact]
    public Task ToLookup_ThenAsQueryable_StaysQuiet() =>
        VerifyQuietAsync("db.Set<User>().Where(u => u.Id > 1).ToLookup(u => u.Id).AsQueryable()");
}
