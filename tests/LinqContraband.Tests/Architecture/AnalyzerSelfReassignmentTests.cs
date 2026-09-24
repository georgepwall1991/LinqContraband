using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Tests.Architecture;

public partial class AnalyzerPerformanceTests
{
    // `query = query.Where(...)` is the most common way to compose an EF Core query, and the
    // assignment starts before the `query` read on its own right-hand side. A resolver that takes
    // "the latest assignment starting before this read" therefore resolves the read to the very
    // assignment it sits in and walks the same receiver chain forever. LC040 did exactly that
    // and hung csc on BTCPay Server, so every analyzer in the package runs over these shapes.
    private static readonly TimeSpan SelfReassignmentTimeout = TimeSpan.FromSeconds(60);

    public static TheoryData<string> AllAnalyzerNames()
    {
        var data = new TheoryData<string>();
        foreach (var type in GetAnalyzerTypes())
            data.Add(type.FullName!);

        return data;
    }

    [Theory]
    [MemberData(nameof(AllAnalyzerNames))]
    public async Task Analyzer_CompletesOnSelfReassignedQueryLocals(string analyzerName)
    {
        var analyzerType = GetAnalyzerTypes().Single(type => type.FullName == analyzerName);
        var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(analyzerType)!;
        var compilation = CreateCompilation(GenerateSelfReassignmentMock(), GenerateSelfReassignmentSource());

        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        await GetDiagnosticsWithinAsync(analyzer, compilation, SelfReassignmentTimeout);
    }

    private static IEnumerable<Type> GetAnalyzerTypes()
    {
        return typeof(LinqContraband.Catalog.RuleCatalog).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract &&
                           typeof(DiagnosticAnalyzer).IsAssignableFrom(type) &&
                           type.GetCustomAttribute<DiagnosticAnalyzerAttribute>() != null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal);
    }

    private static string GenerateSelfReassignmentSource()
    {
        return
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;

            namespace PerfApp;

            public class AppDbContext : DbContext
            {
                public DbSet<User> Users { get; set; }
                public DbSet<Order> Orders { get; set; }
            }

            public class User
            {
                public int Id { get; set; }
                public string Name { get; set; }
                public List<Order> Orders { get; set; }
            }

            public class Order
            {
                public int Id { get; set; }
                public int UserId { get; set; }
                public User User { get; set; }
            }

            public class Queries
            {
                public void Reassigned(AppDbContext db)
                {
                    var query = db.Users.Where(u => u.Id > 0);
                    query = query.Where(u => u.Name != null);
                    query = query.OrderBy(u => u.Id);
                    var tracked = query.ToList();
                    var detached = db.Users.AsNoTracking().ToList();
                    foreach (var user in query)
                        Console.WriteLine(user.Orders.Count);
                }

                public void ReassignedNoTracking(AppDbContext db)
                {
                    IQueryable<User> query = db.Users;
                    query = query.AsNoTracking();
                    query = query.Include(u => u.Orders);
                    var first = query.FirstOrDefault();
                    var tracked = db.Users.First();
                    first.Name = "changed";
                    db.SaveChanges();
                }

                public void ReassignedFromOutVariable(AppDbContext db)
                {
                    if (!TryGetQuery(db, out var query))
                        return;

                    query = query.Where(u => u.Id > 0);
                    var users = query.ToList();
                    var detached = db.Users.AsNoTracking().ToList();
                    db.Users.RemoveRange(query);
                    db.SaveChanges();
                }

                public void ReassignedThroughConditional(AppDbContext db, bool filter)
                {
                    var query = db.Users.AsQueryable();
                    query = filter ? query.Where(u => u.Id > 0) : query;
                    query = query.Where(u => u.Name != null) ?? query;
                    var users = query.Skip(10).ToList();
                    var detached = db.Users.AsNoTracking().ToList();
                }

                public void ReassignedThroughLambda(AppDbContext db)
                {
                    IQueryable<User> query = db.Users;
                    Func<IQueryable<User>> current = () => query;
                    query = current().Where(u => u.Id > 0);
                    var users = query.ToList();
                    var detached = db.Users.AsNoTracking().ToList();
                }

                public void ReassignedInLoop(AppDbContext db, IEnumerable<int> ids)
                {
                    var query = db.Users.AsQueryable();
                    foreach (var id in ids)
                    {
                        query = query.Where(u => u.Id != id);
                        var match = query.FirstOrDefault();
                        var order = db.Orders.Where(o => o.UserId == id).ToList();
                    }

                    var users = query.ToList();
                }

                public void ReassignedMaterialized(AppDbContext db)
                {
                    var users = db.Users.Where(u => u.Id > 0).ToList();
                    users = users.Where(u => u.Name != null).ToList();
                    users = users.OrderBy(u => u.Id).ToList();
                    var detached = db.Users.AsNoTracking().ToList();
                    db.Users.RemoveRange(users);
                    db.SaveChanges();
                }

                public async Task ReassignedAsync(AppDbContext db)
                {
                    var query = db.Orders.Include(o => o.User).AsQueryable();
                    query = query.Where(o => o.Id > 0);
                    query = query.AsNoTracking();
                    var orders = await query.ToListAsync();
                    var tracked = await db.Orders.ToListAsync();
                    foreach (var order in orders)
                        Console.WriteLine(order.User.Name);
                }

                public void ReassignedBeforeBulkDelete(AppDbContext db)
                {
                    var query = db.Users.AsQueryable();
                    query = query.Where(u => u.Id > 0);
                    query.ExecuteDelete();
                }

                public List<User> ReassignedWithPredicateShapes(AppDbContext db, IQueryable<User> source, string name)
                {
                    var query = source;
                    query = query.Where(u => u.Name.ToLower() == name);
                    query = query.Where(u => u.Name.ToUpperInvariant().Contains(name, StringComparison.OrdinalIgnoreCase));
                    query = query.Where(u => u.Id < DateTime.Now.Year);
                    query = query.Where(u => IsActive(u));
                    query = query.OrderBy(u => u.Name).OrderBy(u => u.Id);
                    var projected = query.Select(u => new { u.Id, Orders = u.Orders.ToList() }).ToList();
                    var tracked = db.Users.Where(u => u.Name.ToLower() == name);
                    tracked = tracked.Where(u => u.Name.ToLower() != null);
                    return query.Skip(10).Take(5).ToList();
                }

                private static bool IsActive(User user) => user.Id > 0;

                private static bool TryGetQuery(AppDbContext db, out IQueryable<User> query)
                {
                    query = db.Users;
                    return true;
                }
            }
            """;
    }

    private static string GenerateSelfReassignmentMock()
    {
        return
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Linq;
            using System.Linq.Expressions;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Microsoft.EntityFrameworkCore
            {
                public class DbContext
                {
                    public int SaveChanges() => 0;
                    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
                    public DbSet<TEntity> Set<TEntity>() where TEntity : class => null;
                }

                public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
                {
                    public void RemoveRange(IEnumerable<TEntity> entities) { }
                    public TEntity Find(params object[] keyValues) => null;
                    public Type ElementType => typeof(TEntity);
                    public Expression Expression => null;
                    public IQueryProvider Provider => null;
                    public IEnumerator<TEntity> GetEnumerator() => null;
                    IEnumerator IEnumerable.GetEnumerator() => null;
                }

                public static class EntityFrameworkQueryableExtensions
                {
                    public static IQueryable<TEntity> AsNoTracking<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;
                    public static IQueryable<TEntity> AsTracking<TEntity>(this IQueryable<TEntity> source) where TEntity : class => source;
                    public static IQueryable<TEntity> Include<TEntity, TProperty>(this IQueryable<TEntity> source, Expression<Func<TEntity, TProperty>> navigationPropertyPath) where TEntity : class => source;
                    public static int ExecuteDelete<TSource>(this IQueryable<TSource> source) => 0;
                    public static Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => Task.FromResult(new List<TSource>());
                }
            }
            """;
    }
}
