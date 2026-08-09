using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Analyzers.LC045_MissingInclude;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Tests.Analyzers.LC045_MissingInclude;

/// <summary>
/// The contract every reportable LC045 shape must satisfy: a fix is offered, applying it removes
/// the diagnostic, and the result still <b>emits</b>.
///
/// This exists because 5.7.28 taught the lesson the hard way. Widening what the analyzer reports
/// silently widened what the fixer had to handle, and the fixer went on wrapping the node it was
/// handed — producing <c>select o.Include(...)</c> for every query-syntax finding, which does not
/// compile. Nothing failed, because no test asked the fixer about the newly reported shapes.
/// One shape deliberately stays out: `await foreach (var o in db.Orders)`, whose fix must restore
/// the AsAsyncEnumerable bridge. Modelling it needs a DbSet that is also an IAsyncEnumerable, and
/// adding that to this shared mock makes `Select` ambiguous between the queryable and async query
/// patterns (CS0121/CS1940), which silently stops the query-syntax and identity-projection shapes
/// here from reporting at all. That rewrite is covered by its own test with its own mock, and
/// mutation isolation confirms that test fails when the bridge restoration is removed.
///
/// Adding a shape to the analyzer means adding it here IN THE SAME CHANGE. That is part of the
/// rule, not an optional extra: this corpus went stale for three releases — the widened-source
/// shapes of 5.7.32/5.7.33 and the expression-conditional shapes of 5.7.38 were reportable but
/// never asked about here — which is the same blind spot that let 5.7.28 ship an uncompilable fix.
///
/// Emit is deliberate rather than <see cref="Compilation.GetDiagnostics"/>: binding alone does not
/// surface lowering-phase failures, so a fix can bind and still fail to build.
/// </summary>
public sealed class MissingIncludeFixerCoverageContractTests
{
    private const int MaxFixRounds = 8;

    public static IEnumerable<object[]> ReportableShapes()
    {
        return new[]
        {
        new object[] { "CollectionForeach", @"        var orders = db.Orders.ToList();
        foreach (var o in orders) System.Console.WriteLine(o.Customer.Name);" },
        new object[] { "InlineMaterializer", @"        foreach (var o in db.Orders.ToList()) System.Console.WriteLine(o.Customer.Name);" },
        new object[] { "DirectDbSetForeach", @"        foreach (var o in db.Orders) System.Console.WriteLine(o.Customer.Name);" },
        new object[] { "ElementMaterializer", @"        var order = db.Orders.First();
        System.Console.WriteLine(order.Customer.Name);" },
        new object[] { "ElementExtraction", @"        var orders = db.Orders.ToList();
        var one = orders.First();
        System.Console.WriteLine(one.Customer.Name);" },
        new object[] { "OrderingCallback", @"        var orders = db.Orders.ToList();
        foreach (var o in orders.OrderBy(x => x.Customer.Name)) System.Console.WriteLine(o.Id);" },
        new object[] { "NestedNavigation", @"        var orders = db.Orders.ToList();
        foreach (var o in orders)
        foreach (var i in o.Items) System.Console.WriteLine(i.Product.Name);" },
        new object[] { "NavCollectionCallback", @"        var orders = db.Orders.ToList();
        foreach (var o in orders) System.Console.WriteLine(o.Items.Sum(i => i.Product.Id));" },
        new object[] { "NavCollectionIndexer", @"        var orders = db.Orders.ToList();
        foreach (var o in orders) System.Console.WriteLine(o.Items[0].Product.Name);" },
        new object[] { "NavCollectionCopy", @"        var orders = db.Orders.ToList();
        foreach (var o in orders)
        foreach (var i in o.Items.ToList()) System.Console.WriteLine(i.Product.Name);" },
        new object[] { "CollectionCopy", @"        var orders = db.Orders.ToList();
        foreach (var o in orders.ToList()) System.Console.WriteLine(o.Customer.Name);" },
        new object[] { "IndexedAccess", @"        var orders = db.Orders.ToList();
        System.Console.WriteLine(orders[0].Customer.Name);" },
        new object[] { "ConditionalAccess", @"        var order = db.Orders.FirstOrDefault();
        System.Console.WriteLine(order?.Customer.Name);" },
        new object[] { "ConditionalIndexer", @"        var orders = db.Orders.ToList();
        System.Console.WriteLine(orders?[0].Customer.Name);" },
        new object[] { "HoistedQueryable", @"        var q = db.Orders;
        var orders = q.ToList();
        foreach (var o in orders) System.Console.WriteLine(o.Customer.Name);" },
        new object[] { "NestedPathExtendsChain", @"        var orders = db.Orders.Include(o => o.Customer).ToList();
        foreach (var o in orders) System.Console.WriteLine(o.Customer.Address.City);" },
        new object[] { "StringIncludePresent", @"        var orders = db.Orders.Include(""Items"").ToList();
        foreach (var o in orders) System.Console.WriteLine(o.Customer.Name);" },
        new object[] { "ViewChain", @"        var orders = db.Orders.ToList();
        foreach (var o in orders.Where(x => x.Id > 0).OrderBy(x => x.Id).Take(5)) System.Console.WriteLine(o.Customer.Name);" },
        new object[] { "CollectionAliasLocal", @"        var orders = db.Orders.ToList();
        var active = orders.Where(o => o.Id > 0).ToList();
        foreach (var o in active) System.Console.WriteLine(o.Customer.Name);" },
        new object[] { "AliasOfAlias", @"        var orders = db.Orders.ToList();
        var first = orders.Where(o => o.Id > 0);
        var second = first.Take(5);
        foreach (var o in second) System.Console.WriteLine(o.Customer.Name);" },
        new object[] { "QuerySyntax", @"        var orders = (from o in db.Orders where o.Id > 0 select o).ToList();
        foreach (var o in orders) System.Console.WriteLine(o.Customer.Name);" },
        new object[] { "QuerySyntaxOrderBy", @"        var orders = (from o in db.Orders orderby o.Id select o).ToList();
        foreach (var o in orders) System.Console.WriteLine(o.Customer.Name);" },
        new object[] { "QuerySyntaxElement", @"        var order = (from o in db.Orders where o.Id > 0 select o).First();
        System.Console.WriteLine(order.Customer.Name);" },
        new object[] { "InMemoryQuerySyntaxView", @"        var orders = db.Orders.ToList();
        foreach (var o in from x in orders where x.Id > 0 select x) System.Console.WriteLine(o.Customer.Name);" },
        new object[] { "IdentityProjection", @"        var orders = db.Orders.Select(o => o).ToList();
        foreach (var o in orders) System.Console.WriteLine(o.Customer.Name);" },
        new object[] { "WidenedEnumerableLocalForeach", @"        IEnumerable<Order> source = db.Orders;
        foreach (var o in source) System.Console.WriteLine(o.Customer.Name);" },
        new object[] { "WidenedEnumerableLocalMaterialized", @"        IEnumerable<Order> source = db.Orders;
        var orders = source.ToList();
        foreach (var o in orders) System.Console.WriteLine(o.Customer.Name);" },
        new object[] { "TernaryRead", @"        var orders = db.Orders.ToList();
        foreach (var o in orders) { var s = o.Id > 0 ? o.Customer.Name : """"; System.Console.WriteLine(s); }" },
        new object[] { "SwitchExpressionRead", @"        var orders = db.Orders.ToList();
        foreach (var o in orders) { var s = o.Id switch { 1 => o.Customer.Name, _ => """" }; System.Console.WriteLine(s); }" },
        new object[] { "NullConditionalNavigationRead", @"        var orders = db.Orders.ToList();
        foreach (var o in orders) { var s = o.Customer?.Name; System.Console.WriteLine(s); }" },
        new object[] { "CoalesceNavigationRead", @"        var orders = db.Orders.ToList();
        foreach (var o in orders) { var c = o.Customer ?? new Customer(); System.Console.WriteLine(c.Name); }" },
        new object[] { "IfElseRead", @"        var orders = db.Orders.ToList();
        foreach (var o in orders) { string s; if (o.Id > 0) s = o.Customer.Name; else s = """"; System.Console.WriteLine(s); }" },
        new object[] { "LocalFunctionClosure", @"        var orders = db.Orders.ToList();
        void Print() { foreach (var o in orders) System.Console.WriteLine(o.Customer.Name); }
        Print();" },
        new object[] { "EntityTakingLocalFunction", @"        var orders = db.Orders.ToList();
        void Show(Order o) => System.Console.WriteLine(o.Customer.Name);
        foreach (var o in orders) Show(o);" },
        new object[] { "EntityCalleeSharedByTwoLoops", @"        var orders = db.Orders.ToList();
        void Show(Order o) => System.Console.WriteLine(o.Customer.Name);
        foreach (var o in orders) Show(o);
        foreach (var o in orders) Show(o);" },
        new object[] { "PrivateMethodCallee", @"        var orders = db.Orders.ToList();
        foreach (var o in orders) RenderShape(o);" },
        new object[] { "AwaitForeachBridge", @"        await foreach (var o in db.Orders.AsAsyncEnumerable()) System.Console.WriteLine(o.Customer.Name);" },
        new object[] { "AwaitForeachTernary", @"        await foreach (var o in db.Orders.AsAsyncEnumerable()) { var s = o.Id > 0 ? o.Customer.Name : """"; System.Console.WriteLine(s); }" },
        new object[] { "AwaitForeachNestedPath", @"        await foreach (var o in db.Orders.Include(x => x.Customer).AsAsyncEnumerable()) System.Console.WriteLine(o.Customer.Address.City);" }
        };
    }

    [Theory]
    [MemberData(nameof(ReportableShapes))]
    public async Task EveryReportableShape_HasAFixThatCompilesAndClearsTheDiagnostic(
        string shapeName,
        string body)
    {
        var source = BuildSource(body);
        var document = CreateDocument(source);

        var diagnostics = await GetMissingIncludeDiagnosticsAsync(document);
        Assert.True(
            diagnostics.Length > 0,
            $"{shapeName}: expected LC045 to report, so the shape belongs in this corpus.");

        // Fix the first remaining finding and re-ask, because one Include can legitimately
        // cover a later path (Include then ThenInclude on the same chain).
        var fixedDocument = document;
        for (var round = 0; round < MaxFixRounds; round++)
        {
            var remainingNow = await GetMissingIncludeDiagnosticsAsync(fixedDocument);
            if (remainingNow.Length == 0)
                break;

            var action = await GetSingleCodeActionAsync(fixedDocument, remainingNow[0]);
            Assert.True(
                action != null,
                $"{shapeName}: LC045 reported '{remainingNow[0].GetMessage()}' but offered no fix.");

            fixedDocument = await ApplyAsync(fixedDocument, action!);
        }

        var remaining = await GetMissingIncludeDiagnosticsAsync(fixedDocument);
        Assert.True(
            remaining.Length == 0,
            $"{shapeName}: LC045 still reports after applying every offered fix: "
                + string.Join(", ", remaining.Select(entry => entry.GetMessage())));

        await AssertEmitsAsync(shapeName, fixedDocument);
    }

    private static async Task AssertEmitsAsync(string shapeName, Document document)
    {
        var compilation = await document.Project.GetCompilationAsync();
        Assert.NotNull(compilation);

        using var stream = new MemoryStream();
        var result = compilation!.Emit(stream);
        var errors = result
            .Diagnostics.Where(entry => entry.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            $"{shapeName}: the fixed document does not build: "
                + string.Join(
                    Environment.NewLine,
                    errors.Select(entry => entry.ToString()))
                + Environment.NewLine
                + (await document.GetTextAsync()).ToString());
    }

    private static async Task<CodeAction?> GetSingleCodeActionAsync(
        Document document,
        Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await new MissingIncludeFixer().RegisterCodeFixesAsync(context);
        return actions.Count == 0 ? null : actions[0];
    }

    /// <summary>
    /// Rebuilds the document from the fixed text rather than carrying the fix's changed solution
    /// forward. The solution a code action produces is derived from the workspace it was created
    /// against, and re-hosting it silently yielded a compilation that reported no errors for text
    /// that plainly does not compile — which would have made the emit assertion below vacuous.
    /// A fresh document from the fixed text is the same thing a user would build.
    /// </summary>
    private static async Task<Document> ApplyAsync(Document document, CodeAction action)
    {
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var applied = operations.OfType<ApplyChangesOperation>().Single();
        var changed = applied.ChangedSolution.GetDocument(document.Id)!;
        var text = await changed.GetTextAsync();
        return CreateDocument(text.ToString());
    }

    private static async Task<ImmutableArray<Diagnostic>> GetMissingIncludeDiagnosticsAsync(
        Document document)
    {
        var compilation = await document.Project.GetCompilationAsync();
        var withAnalyzers = compilation!.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new MissingIncludeAnalyzer()));
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);
        return diagnostics
            .Where(entry => entry.Id == MissingIncludeAnalyzer.DiagnosticId)
            .OrderBy(entry => entry.Location.SourceSpan.Start)
            .ToImmutableArray();
    }

    private static Document CreateDocument(string source)
    {
        var trustedPlatformAssemblies =
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));

        var workspace = new AdhocWorkspace();
        var project = workspace
            .AddProject("FixerCoverage", LanguageNames.CSharp)
            .WithCompilationOptions(
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))
            .WithMetadataReferences(trustedPlatformAssemblies);

        return project.AddDocument("Shape.cs", SourceText.From(source));
    }

    private static string BuildSource(string body)
    {
        // An await-bearing shape needs an async signature; the bridge-restoring rewrite it
        // exercises is the most intricate fix the rule performs.
        var signature = body.Contains("await ") ? "async Task Main()" : "void Main()";
        // A shape may call a private helper; declare one the corpus can use.
        const string helper =
            "\n    private void RenderShape(Order o) => System.Console.WriteLine(o.Customer.Name);\n";
        return Usings
            + @"
class Program
{
    "
            + signature
            + @"
    {
        var db = new MyDbContext();
"
            + body
            + @"
    }
"
            + helper
            + @"}
"
            + MockNamespace;
    }

    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using TestNamespace;
";

    private const string MockNamespace = @"
namespace Microsoft.EntityFrameworkCore.Query
{
    public interface IIncludableQueryable<out TEntity, out TProperty> : IQueryable<TEntity> { }
}

namespace Microsoft.EntityFrameworkCore
{
    using Microsoft.EntityFrameworkCore.Query;

    public class DbContext : IDisposable
    {
        public void Dispose() { }
        public int SaveChanges() => 0;
        public DbSet<T> Set<T>() where T : class => null;
    }

    public class DbSet<T> : IQueryable<T> where T : class
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<TEntity> Include<TEntity>(
            this IQueryable<TEntity> source,
            string navigationPropertyPath)
            => source;

        public static IIncludableQueryable<TEntity, TProperty> Include<TEntity, TProperty>(
            this IQueryable<TEntity> source,
            System.Linq.Expressions.Expression<Func<TEntity, TProperty>> navigationPropertyPath)
            => null;

        public static IIncludableQueryable<TEntity, TProperty> ThenInclude<TEntity, TPreviousProperty, TProperty>(
            this IIncludableQueryable<TEntity, IEnumerable<TPreviousProperty>> source,
            System.Linq.Expressions.Expression<Func<TPreviousProperty, TProperty>> navigationPropertyPath)
            => null;

        public static IIncludableQueryable<TEntity, TProperty> ThenInclude<TEntity, TPreviousProperty, TProperty>(
            this IIncludableQueryable<TEntity, TPreviousProperty> source,
            System.Linq.Expressions.Expression<Func<TPreviousProperty, TProperty>> navigationPropertyPath)
            => null;

        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
        public static IAsyncEnumerable<T> AsAsyncEnumerable<T>(this IQueryable<T> source) => null;
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => null;
    }
}

namespace TestNamespace
{
    using Microsoft.EntityFrameworkCore;

    public class Order
    {
        public int Id { get; set; }
        public string Status { get; set; }
        public Customer Customer { get; set; }
        public Customer @event { get; set; }
        public List<OrderItem> Items { get; set; }
    }

    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Address Address { get; set; }
    }

    public class Address
    {
        public int Id { get; set; }
        public string City { get; set; }
    }

    public class OrderItem
    {
        public int Id { get; set; }
        public string Sku { get; set; }
        public Product Product { get; set; }
    }

    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class MyDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Address> Addresses { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Product> Products { get; set; }
    }

    public class SetOnlyDbContext : DbContext
    {
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Address> Addresses { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
    }
}";
}
