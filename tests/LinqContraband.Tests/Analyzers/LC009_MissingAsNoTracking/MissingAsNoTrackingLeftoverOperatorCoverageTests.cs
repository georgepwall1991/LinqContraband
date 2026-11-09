using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC009_MissingAsNoTracking;

/// <summary>
/// Leftover LC009 <c>ElementPreservingOperators</c> names the shipped
/// <c>categories.First(...)</c> attached-entity fixtures do not isolate.
/// Dropping leftover <c>Where</c> FNs only
/// <see cref="Where_AssignedToCollectionNavigation_StaysQuiet"/>; the shipped
/// <c>First</c> lookup stays green. The same holds for leftover
/// <c>SkipWhile</c>, <c>TakeWhile</c>, <c>Reverse</c>, <c>ToHashSet</c>,
/// <c>ToDictionary</c>, <c>ToLookup</c>, <c>MinBy</c>, <c>MaxBy</c> and
/// <c>DistinctBy</c>. <c>MinBy</c>/<c>MaxBy</c>/<c>DistinctBy</c>/<c>ToHashSet</c>
/// run on Net80 because the default VerifyCS references do not declare those
/// .NET 6 / .NET Standard 2.1 Enumerable methods.
/// <c>Enumerable.Reverse(categories)</c> isolates leftover <c>Reverse</c>
/// because <c>List&lt;T&gt;.Reverse()</c> is a void mutator and cannot be
/// assigned. <c>ThenBy</c>/<c>ThenByDescending</c> stay unpinned: they
/// require <c>OrderBy</c>, which is also in the set, so they cannot isolate.
/// A leftover name on an unrelated collection still reports, and
/// <c>ToDictionary</c> with an <c>elementSelector</c> still reports because
/// that overload replaces the loaded entities.
/// </summary>
public sealed class MissingAsNoTrackingLeftoverOperatorCoverageTests
{
    private const string Mocks = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : System.IDisposable
    {
        public int SaveChanges() => 0;
        public void Dispose() { }
    }

    public class DbSet<T> : System.Linq.IQueryable<T> where T : class
    {
        public System.Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => null;
        public System.Linq.IQueryProvider Provider => null;
        public System.Collections.Generic.IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }
}

namespace Shop
{
    public class Category { public int Id { get; set; } public string Alias { get; set; } }

    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Category Category { get; set; }
        public System.Collections.Generic.List<Category> Categories { get; set; } = new System.Collections.Generic.List<Category>();
        public System.Collections.Generic.IEnumerable<Category> Related { get; set; }
    }

    public class ShopContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Category> Categories { get; set; }
        public Microsoft.EntityFrameworkCore.DbSet<Product> Products { get; set; }
    }
}
";

    private const string Usings = @"using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Shop;
";

    private static string Code(string body) => Usings + @"
class Service
{
    private readonly ShopContext db = new ShopContext();

    void Seed()
    {
        " + body + @"
    }

    void Show(object value) { }
}
" + Mocks;

    private static Task VerifyQuietAsync(string body) =>
        VerifyCS.VerifyAnalyzerAsync(Code(body));

    private static Task VerifyQuietOnNet80Async(string body)
    {
        var test = new CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer,
            XUnitVerifier>
        {
            TestCode = Code(body),
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        return test.RunAsync();
    }

    private static string AttachLookup(string lookup) =>
        @"var categories = db.Categories.ToList();
        var product = new Product { Category = " + lookup + @" };
        Show(product);";

    private static string AttachRelated(string sequence) =>
        @"var categories = db.Categories.ToList();
        var product = new Product { Related = " + sequence + @" };
        Show(product);";

    [Fact]
    public Task Where_AssignedToCollectionNavigation_StaysQuiet() =>
        VerifyQuietAsync(AttachRelated(@"categories.Where(c => c.Alias == ""books"")"));

    [Fact]
    public Task SkipWhile_AssignedToCollectionNavigation_StaysQuiet() =>
        VerifyQuietAsync(AttachRelated(@"categories.SkipWhile(c => c.Alias == ""x"")"));

    [Fact]
    public Task TakeWhile_AssignedToCollectionNavigation_StaysQuiet() =>
        VerifyQuietAsync(AttachRelated(@"categories.TakeWhile(c => c.Alias == ""books"")"));

    [Fact]
    public Task Reverse_AssignedToCollectionNavigation_StaysQuiet() =>
        VerifyQuietAsync(AttachRelated("Enumerable.Reverse(categories)"));

    [Fact]
    public Task ToHashSet_AssignedToCollectionNavigation_StaysQuiet() =>
        VerifyQuietOnNet80Async(AttachRelated("categories.ToHashSet()"));

    [Fact]
    public Task ToDictionary_IndexerLookup_StaysQuiet() =>
        VerifyQuietAsync(AttachLookup(@"categories.ToDictionary(c => c.Alias)[""books""]"));

    [Fact]
    public Task ToLookup_IndexerAssignedToCollectionNavigation_StaysQuiet() =>
        VerifyQuietAsync(AttachRelated(@"categories.ToLookup(c => c.Alias)[""books""]"));

    [Fact]
    public Task MinBy_Lookup_StaysQuiet() =>
        VerifyQuietOnNet80Async(AttachLookup("categories.MinBy(c => c.Id)"));

    [Fact]
    public Task MaxBy_Lookup_StaysQuiet() =>
        VerifyQuietOnNet80Async(AttachLookup("categories.MaxBy(c => c.Id)"));

    [Fact]
    public Task DistinctBy_AssignedToCollectionNavigation_StaysQuiet() =>
        VerifyQuietOnNet80Async(AttachRelated("categories.DistinctBy(c => c.Alias)"));

    [Fact]
    public Task LookalikeWhere_OnUnrelatedCollection_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Code(@"var categories = {|LC009:db.Categories.ToList()|};
        var others = new List<Category>();
        var product = new Product { Related = others.Where(c => c.Alias == ""books"") };
        Show(product);
        Show(categories);"));

    [Fact]
    public Task ToDictionary_WithElementSelector_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Code(@"var categories = {|LC009:db.Categories.ToList()|};
        var product = new Product { Category = categories.ToDictionary(c => c.Alias, c => new Category { Id = c.Id })[""books""] };
        Show(product);"));
}
