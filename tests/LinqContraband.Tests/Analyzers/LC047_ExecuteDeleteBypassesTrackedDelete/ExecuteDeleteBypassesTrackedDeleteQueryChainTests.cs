using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete.ExecuteDeleteBypassesTrackedDeleteAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

public partial class ExecuteDeleteBypassesTrackedDeleteTests
{
    private const string SoftDeletePipeline = @"
    public interface ISoftDelete { bool IsDeleted { get; set; } }
    public sealed class User : ISoftDelete
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
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
    public async Task ExecuteDelete_WithTagWithChain_ShouldTrigger()
    {
        var test = App(SoftDeletePipeline + @"
    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Users.TagWith(""purge"").ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithIgnoreQueryFiltersChain_ShouldTrigger()
    {
        var test = App(SoftDeletePipeline + @"
    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Users.IgnoreQueryFilters().ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithDistinctChain_ShouldTrigger()
    {
        var test = App(SoftDeletePipeline + @"
    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Users.Distinct().ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithReverseChain_ShouldTrigger()
    {
        var test = App(SoftDeletePipeline + @"
    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = {|LC047:db.Users.Reverse().ExecuteDelete()|};
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_WithProjectLookalikeTagWith_ShouldNotTrigger()
    {
        var test = App(SoftDeletePipeline + @"
    public static class QueryLookalikes
    {
        public static IQueryable<TSource> TagWith<TSource>(this IQueryable<TSource> source, string tag) => source;
    }

    public sealed class Program
    {
        public void Run(AppDbContext db)
        {
            var result = QueryLookalikes.TagWith(db.Users, ""purge"").ExecuteDelete();
        }
    }
");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
