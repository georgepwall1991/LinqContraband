using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC012_OptimizeRemoveRange.OptimizeRemoveRangeAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC012_OptimizeRemoveRange;

public partial class OptimizeRemoveRangeAnalyzerTests
{
    [Fact]
    public async Task RemoveRange_FollowedBySaveChanges_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);

            db.Users.RemoveRange(usersToDelete);
            db.SaveChanges();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_InNestedBlockFollowedByOuterSaveChanges_ShouldNotTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main(bool shouldDelete)
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);

            if (shouldDelete)
            {
                db.Users.RemoveRange(usersToDelete);
            }

            db.SaveChanges();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_FollowedBySaveChangesOnDifferentContext_ShouldTrigger()
    {
        // db2.SaveChanges() never commits db1's pending removals, so it is not the
        // unit-of-work pattern the suppression exists for.
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main()
        {
            using var db1 = new AppDbContext();
            using var db2 = new AppDbContext();
            var usersToDelete = db1.Users.Where(u => u.Id > 10);

            {|LC012:db1.Users.RemoveRange(usersToDelete)|};
            db2.SaveChanges();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DbContextRemoveRange_FollowedBySaveChangesOnDifferentContext_ShouldTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main()
        {
            using var db1 = new AppDbContext();
            using var db2 = new AppDbContext();
            var usersToDelete = db1.Users.Where(u => u.Id > 10);

            {|LC012:db1.RemoveRange(usersToDelete)|};
            db2.SaveChanges();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WithSaveChangesInMutuallyExclusiveElseBranch_ShouldTrigger()
    {
        // The else-branch save can never run after this RemoveRange in the same execution,
        // so it cannot be committing these removals.
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main(bool purge)
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);

            if (purge)
            {
                {|LC012:db.Users.RemoveRange(usersToDelete)|};
            }
            else
            {
                db.SaveChanges();
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WithSaveChangesInOtherSwitchSection_ShouldTrigger()
    {
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main(int mode)
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);

            switch (mode)
            {
                case 1:
                    {|LC012:db.Users.RemoveRange(usersToDelete)|};
                    break;
                case 2:
                    db.SaveChanges();
                    break;
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_FollowedBySaveChangesOnAliasedContext_ShouldNotTrigger()
    {
        // db2 is the same instance as db1, so its SaveChanges commits the pending removals;
        // alias chains must resolve to the same creation before a save is dismissed.
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main()
        {
            using var db1 = new AppDbContext();
            var db2 = db1;
            var usersToDelete = db1.Users.Where(u => u.Id > 10);

            db1.Users.RemoveRange(usersToDelete);
            db2.SaveChanges();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_FollowedBySaveChangesOnUnresolvableContext_ShouldNotTrigger()
    {
        // A context parameter cannot be proven distinct from a fresh local (the caller could
        // have passed the same instance through a factory), so the save still suppresses.
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main(AppDbContext other)
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);

            db.Users.RemoveRange(usersToDelete);
            other.SaveChanges();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_WithGotoCaseReachingSaveChangesSection_ShouldNotTrigger()
    {
        // goto case lets section 1 flow into section 2, so the save does run after the
        // removals. Switch sections with explicit jumps are not exclusive.
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main(int mode)
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);

            switch (mode)
            {
                case 1:
                    db.Users.RemoveRange(usersToDelete);
                    goto case 2;
                case 2:
                    db.SaveChanges();
                    break;
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_OnDbSetParameter_FollowedBySaveChanges_ShouldNotTrigger()
    {
        // The DbSet arrives as a parameter, so its owning context is unknowable; any later
        // save must conservatively count as the unit-of-work commit.
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main(DbSet<User> users)
        {
            using var db = new AppDbContext();
            var usersToDelete = users.Where(u => u.Id > 10);

            users.RemoveRange(usersToDelete);
            db.SaveChanges();
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RemoveRange_InTryWithSaveChangesInCatch_ShouldNotTrigger()
    {
        // Not an exclusive pair for this rule: the try may throw after RemoveRange ran, and
        // a catch-side save on the same context would still commit the pending removals.
        var test = Usings + @"
namespace TestApp
{
    public class AppDbContext : DbContext {}

    public class Program
    {
        public void Main()
        {
            using var db = new AppDbContext();
            var usersToDelete = db.Users.Where(u => u.Id > 10);

            try
            {
                db.Users.RemoveRange(usersToDelete);
            }
            catch (Exception)
            {
                db.SaveChanges();
            }
        }
    }
}" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
