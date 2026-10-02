using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer,
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingFixer>;

namespace LinqContraband.Tests.Analyzers.LC009_MissingAsNoTracking;

/// <summary>
/// Real-world shapes where the tracked query is deliberate: an explicit tracking switch (Oqtane), a raw SQL write
/// with RETURNING/OUTPUT, and a loaded entity attached to a newly created entity (Smartstore seed data), which
/// must stay tracked or EF Core inserts a duplicate.
/// </summary>
public class MissingAsNoTrackingDeliberateTrackingTests
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

    public static class EntityFrameworkQueryableExtensions
    {
        public static System.Linq.IQueryable<T> AsNoTracking<T>(this System.Linq.IQueryable<T> source) => source;
    }

    public static class RelationalQueryableExtensions
    {
        public static System.Linq.IQueryable<T> FromSqlRaw<T>(this DbSet<T> source, string sql, params object[] parameters) where T : class => source;
        public static System.Linq.IQueryable<T> FromSqlInterpolated<T>(this DbSet<T> source, System.FormattableString sql) where T : class => source;
        public static System.Linq.IQueryable<T> FromSql<T>(this DbSet<T> source, System.FormattableString sql) where T : class => source;
    }
}

namespace Shop
{
    public class Category { public int Id { get; set; } public string Alias { get; set; } }

    public class Order
    {
        public int Id { get; set; }
        public string Status { get; set; }
    }

    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Category Category { get; set; }
        public System.Collections.Generic.List<Category> Categories { get; set; } = new System.Collections.Generic.List<Category>();
    }

    public class ShopContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Order> Orders { get; set; }
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

    private static string Code(string members) => Usings + @"
class Service
{
    private readonly ShopContext db = new ShopContext();
    private bool flag;

" + members + @"

    void Show(object value) { }
}
" + Mocks;

    private static Task VerifyAsync(string members) =>
        VerifyFix.VerifyAnalyzerAsync(Code(members));

    // ---- explicit tracking switch (Oqtane) ----

    [Fact]
    public Task TrackingParameter_IfReturn_TrackedBranchIsQuiet() => VerifyAsync(@"
    Order Get(int id, bool tracking)
    {
        using var ctx = new ShopContext();
        if (tracking)
            return ctx.Orders.First(o => o.Id == id);
        return ctx.Orders.AsNoTracking().First(o => o.Id == id);
    }");

    [Fact]
    public Task TrackingParameter_Ternary_TrackedArmIsQuiet() => VerifyAsync(@"
    Order Get(int id, bool tracking)
    {
        using var ctx = new ShopContext();
        return tracking
            ? ctx.Orders.First(o => o.Id == id)
            : ctx.Orders.AsNoTracking().First(o => o.Id == id);
    }");

    [Fact]
    public Task TrackChangesParameter_TrackedQueryIsQuiet() => VerifyAsync(@"
    string Get(int id, bool trackChanges)
    {
        if (trackChanges)
        {
            var order = db.Orders.First(o => o.Id == id);
            return order.Status;
        }
        return null;
    }");

    [Fact]
    public Task UntrackedSiblingInElse_SameSet_TrackedBranchIsQuiet() => VerifyAsync(@"
    string Get(int id, bool forEdit)
    {
        Order order;
        if (forEdit)
            order = db.Orders.First(o => o.Id == id);
        else
            order = db.Orders.AsNoTracking().First(o => o.Id == id);
        return order.Status;
    }");

    [Fact]
    public Task UntrackedSiblingAfterIfReturn_SameSet_TrackedBranchIsQuiet() => VerifyAsync(@"
    Order Get(int id, bool forEdit)
    {
        using var ctx = new ShopContext();
        if (forEdit)
            return ctx.Orders.First(o => o.Id == id);
        return ctx.Orders.AsNoTracking().First(o => o.Id == id);
    }");

    [Fact]
    public Task UntrackedSiblingInSwitchSection_SameSet_TrackedSectionIsQuiet() => VerifyAsync(@"
    string Get(int id, int mode)
    {
        Order order;
        switch (mode)
        {
            case 0:
                order = db.Orders.First(o => o.Id == id);
                break;
            default:
                order = db.Orders.AsNoTracking().First(o => o.Id == id);
                break;
        }
        return order.Status;
    }");

    [Fact]
    public Task UntrackedEarlyReturn_ThenTrackedQuery_IsQuiet() => VerifyAsync(@"
    Order Get(int id, bool forEdit)
    {
        using var ctx = new ShopContext();
        if (!forEdit)
            return ctx.Orders.AsNoTracking().First(o => o.Id == id);
        return ctx.Orders.First(o => o.Id == id);
    }");

    [Fact]
    public Task UntrackedSiblingInSwitchExpressionArm_IsQuiet() => VerifyAsync(@"
    Order Get(int id, int mode)
    {
        using var ctx = new ShopContext();
        return mode switch
        {
            0 => ctx.Orders.First(o => o.Id == id),
            _ => ctx.Orders.AsNoTracking().First(o => o.Id == id),
        };
    }");

    [Fact]
    public Task LocalContext_ReturnedFromBranch_NoUntrackedSibling_StillReports() => VerifyAsync(@"
    Order Get(int id, bool forEdit)
    {
        using var ctx = new ShopContext();
        if (forEdit)
            return {|LC009:ctx.Orders.First(o => o.Id == id)|};
        return null;
    }");

    [Fact]
    public Task UntrackedQueryInNonJumpingIf_DoesNotExcuseLaterTrackedQuery() => VerifyAsync(@"
    Order Get(int id, bool forEdit)
    {
        using var ctx = new ShopContext();
        if (forEdit)
            Show(ctx.Orders.AsNoTracking().Count());
        return {|LC009:ctx.Orders.First(o => o.Id == id)|};
    }");

    [Fact]
    public Task UnrelatedCondition_NoUntrackedSibling_StillReports() => VerifyAsync(@"
    string Get(int id, bool forEdit)
    {
        if (forEdit)
        {
            var order = {|LC009:db.Orders.First(o => o.Id == id)|};
            return order.Status;
        }
        return null;
    }");

    [Fact]
    public Task UntrackedSiblingOverDifferentSet_StillReports() => VerifyAsync(@"
    string Get(int id, bool forEdit)
    {
        if (forEdit)
        {
            var order = {|LC009:db.Orders.First(o => o.Id == id)|};
            return order.Status;
        }
        var category = db.Categories.AsNoTracking().First(c => c.Id == id);
        return category.Alias;
    }");

    [Fact]
    public Task NonBoolTrackNamedCondition_StillReports() => VerifyAsync(@"
    string Get(int id, string trackingCode)
    {
        if (trackingCode != null)
        {
            var order = {|LC009:db.Orders.First(o => o.Id == id)|};
            return order.Status;
        }
        return null;
    }");

    // ---- raw SQL writes ----

    [Fact]
    public Task FromSqlRaw_UpdateReturning_IsQuiet() => VerifyAsync(@"
    string Run(int id)
    {
        var order = db.Orders.FromSqlRaw(""UPDATE orders SET status = 'x' WHERE id = {0} RETURNING *"", id).ToList();
        return order[0].Status;
    }");

    [Fact]
    public Task FromSqlRaw_LeadingCommentAndWhitespace_DeleteReturning_IsQuiet() => VerifyAsync(@"
    int Run(int id)
    {
        var orders = db.Orders.FromSqlRaw(""  -- purge\n /* old */ delete from orders where id = {0} returning *"", id).ToList();
        return orders.Count;
    }");

    [Fact]
    public Task FromSqlInterpolated_InsertOutput_IsQuiet() => VerifyAsync(@"
    int Run(string status)
    {
        var orders = db.Orders.FromSqlInterpolated($""INSERT INTO orders (status) OUTPUT INSERTED.* VALUES ({status})"").ToList();
        return orders.Count;
    }");

    [Fact]
    public Task FromSql_Merge_IsQuiet() => VerifyAsync(@"
    int Run(string status)
    {
        var orders = db.Orders.FromSql($""MERGE INTO orders USING x ON 1 = 1 WHEN MATCHED THEN UPDATE SET status = {status} OUTPUT inserted.*;"").ToList();
        return orders.Count;
    }");

    [Fact]
    public Task FromSqlRaw_Select_StillReports() => VerifyAsync(@"
    int Run(int id)
    {
        var orders = {|LC009:db.Orders.FromSqlRaw(""SELECT * FROM orders -- UPDATE later"").ToList()|};
        return orders.Count;
    }");

    [Fact]
    public Task FromSqlRaw_NonConstantSql_StillReports() => VerifyAsync(@"
    int Run(string sql)
    {
        var orders = {|LC009:db.Orders.FromSqlRaw(sql).ToList()|};
        return orders.Count;
    }");

    // ---- loaded entity attached to a new entity (Smartstore seed data) ----

    [Fact]
    public Task EntityInNewObjectInitializerNavigation_IsQuiet() => VerifyAsync(@"
    void Seed()
    {
        var category = db.Categories.First(c => c.Alias == ""books"");
        var product = new Product { Name = ""Novel"", Category = category };
        Show(product);
    }");

    [Fact]
    public Task EntityAssignedToNavigationOfNewLocal_IsQuiet() => VerifyAsync(@"
    void Seed()
    {
        var category = db.Categories.First(c => c.Alias == ""books"");
        var product = new Product();
        product.Category = category;
        Show(product);
    }");

    [Fact]
    public Task InMemoryLookupInNewObjectInitializer_IsQuiet() => VerifyAsync(@"
    List<Product> Seed()
    {
        var categories = db.Categories.ToList();
        return new List<Product>
        {
            new Product { Name = ""Novel"", Category = categories.First(c => c.Alias == ""books"") },
            new Product { Name = ""Album"", Category = categories.First(c => c.Alias == ""music"") },
        };
    }");

    [Fact]
    public Task EntityAddedToCollectionOfNewObject_IsQuiet() => VerifyAsync(@"
    void Seed()
    {
        var category = db.Categories.First(c => c.Alias == ""books"");
        var product = new Product();
        product.Categories.Add(category);
        Show(product);
    }");

    [Fact]
    public Task EntityInCollectionInitializerOfNewObject_IsQuiet() => VerifyAsync(@"
    void Seed()
    {
        var category = db.Categories.First(c => c.Alias == ""books"");
        var product = new Product { Categories = { category } };
        Show(product);
    }");

    [Fact]
    public Task ScalarOfEntityInNewObject_StillReports() => VerifyAsync(@"
    void Seed()
    {
        var category = {|LC009:db.Categories.First(c => c.Alias == ""books"")|};
        var product = new Product { Name = category.Alias };
        Show(product);
    }");

    [Fact]
    public Task EntityAssignedToNavigationOfExistingObject_StillReports() => VerifyAsync(@"
    void Link(Product product)
    {
        var category = {|LC009:db.Categories.First(c => c.Alias == ""books"")|};
        product.Category = category;
    }");

    [Fact]
    public Task NegatedTrackingFlag_QueryInUntrackedBranch_StillReports() => VerifyAsync(@"
    string Get(int id, bool tracking)
    {
        if (!tracking)
        {
            var order = {|LC009:db.Orders.First(o => o.Id == id)|};
            return order.Status;
        }
        return null;
    }");

    [Fact]
    public Task TrackingFlagEarlyReturn_QueryAfterUntrackedExit_StillReports() => VerifyAsync(@"
    string Get(int id, bool tracking)
    {
        if (tracking)
            return null;
        var order = {|LC009:db.Orders.First(o => o.Id == id)|};
        return order.Status;
    }");

    [Fact]
    public Task NegatedTrackingFlag_QueryInElseBranch_IsQuiet() => VerifyAsync(@"
    string Get(int id, bool tracking)
    {
        if (!tracking)
            return null;
        else
        {
            var order = db.Orders.First(o => o.Id == id);
            return order.Status;
        }
    }");

    [Fact]
    public Task ProjectedCopyInNewObject_StillReports() => VerifyAsync(@"
    void Seed()
    {
        var categories = {|LC009:db.Categories.ToList()|};
        var product = new Product { Category = categories.Select(c => new Category { Id = c.Id }).First() };
        Show(product);
    }");

    [Fact]
    public Task NewLocalReassignedToExistingObject_StillReports() => VerifyAsync(@"
    void Link(Product existing)
    {
        var category = {|LC009:db.Categories.First(c => c.Alias == ""books"")|};
        var product = new Product();
        product = existing;
        product.Category = category;
    }");
}
