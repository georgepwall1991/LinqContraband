using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete.ExecuteDeleteBypassesTrackedDeleteAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

public partial class ExecuteDeleteBypassesTrackedDeleteTests
{
    // Local extras so this file does not edit the shared LC047 EfMock used by
    // other open coverage PRs. ThenBy / ThenByDescending / ThenInclude normally
    // need a predecessor OrderBy / Include (dual-reason). Mocking them on
    // IQueryable isolates each name so dropping it from
    // IsSingleSourceTransparentQueryMethod fails only that fixture.
    private const string ThenOperatorQueryableExtensions = @"
namespace Microsoft.EntityFrameworkCore
{
    public static class ThenOperatorQueryableExtensions
    {
        public static System.Linq.IQueryable<TSource> ThenBy<TSource, TKey>(
            this System.Linq.IQueryable<TSource> source,
            System.Linq.Expressions.Expression<System.Func<TSource, TKey>> keySelector) => source;

        public static System.Linq.IQueryable<TSource> ThenByDescending<TSource, TKey>(
            this System.Linq.IQueryable<TSource> source,
            System.Linq.Expressions.Expression<System.Func<TSource, TKey>> keySelector) => source;

        public static System.Linq.IQueryable<TEntity> ThenInclude<TEntity, TProperty>(
            this System.Linq.IQueryable<TEntity> source,
            System.Linq.Expressions.Expression<System.Func<TEntity, TProperty>> navigationPropertyPath)
            where TEntity : class => source;
    }
}
";

    private const string ThenOperatorConversionPipeline = @"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class Order { public int Id { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
        public System.Collections.Generic.ICollection<Order> Orders { get; set; }
    }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    ((ISoftDelete)entry.Entity).IsDeleted = true;
                }
            }
            return base.SaveChanges();
        }
    }
";

    [Fact]
    public async Task ExecuteDelete_WithThenByChain_ShouldTrigger()
    {
        var test = AppWithThenOperators(@"
    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Users.ThenBy(u => u.Id).ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithThenByDescendingChain_ShouldTrigger()
    {
        var test = AppWithThenOperators(@"
    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Users.ThenByDescending(u => u.Id).ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithThenIncludeChain_ShouldTrigger()
    {
        var test = AppWithThenOperators(@"
    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Users.ThenInclude(u => u.Orders).ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    private static string AppWithThenOperators(string body) =>
        @"using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Threading.Tasks;
" + EfMock + ThenOperatorQueryableExtensions + @"
namespace TestApp
{
" + ThenOperatorConversionPipeline + body + @"
}";
}
