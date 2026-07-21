using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations.ConcurrentDbContextOperationsAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC046_ConcurrentDbContextOperations;

public sealed class ConcurrentDbContextOperationsContextIdentityTests
{
    private const string EfMock = ConcurrentDbContextOperationsTests.EfMock;

    [Fact]
    public async Task DifferentReceiverReadonlyContextFields_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public readonly AppDbContext Db = new AppDbContext();
    }

    public sealed class Program
    {
        public async Task Run(Holder first, Holder second)
        {
            await Task.WhenAll(
                first.Db.Users.ToListAsync(),
                second.Db.Users.AnyAsync());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DifferentReceiverStableContextProperties_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public AppDbContext Context { get; } = new AppDbContext();
    }

    public sealed class Program
    {
        public async Task Run(Holder first, Holder second)
        {
            await Task.WhenAll(
                first.Context.Users.ToListAsync(),
                second.Context.Users.AnyAsync());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ReassignedReceiverAlias_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public AppDbContext Context { get; } = new AppDbContext();
    }

    public sealed class Program
    {
        public async Task Run(Holder first, Holder second)
        {
            var alias = first;
            alias = second;
            await Task.WhenAll(
                first.Context.Users.ToListAsync(),
                alias.Context.Users.AnyAsync());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SameReceiverReadonlyContextField_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public readonly AppDbContext Db = new AppDbContext();
    }

    public sealed class Program
    {
        public async Task Run(Holder holder)
        {
            await Task.WhenAll(
                {|#0:holder.Db.Users.ToListAsync()|},
                {|#1:holder.Db.Users.AnyAsync()|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("Db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ReceiverAliasToSameObjectField_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public readonly AppDbContext Db = new AppDbContext();
    }

    public sealed class Program
    {
        public async Task Run(Holder holder)
        {
            var alias = holder;
            await Task.WhenAll(
                {|#0:holder.Db.Users.ToListAsync()|},
                {|#1:alias.Db.Users.AnyAsync()|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("Db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StaticReadonlyContextField_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        private static readonly AppDbContext Db = new AppDbContext();

        public async Task Run()
        {
            await Task.WhenAll(
                {|#0:Db.Users.ToListAsync()|},
                {|#1:Db.Users.AnyAsync()|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("Db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DifferentContextMembersOnSameReceiver_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Holder
    {
        public readonly AppDbContext ReadDb = new AppDbContext();
        public readonly AppDbContext WriteDb = new AppDbContext();
    }

    public sealed class Program
    {
        public async Task Run(Holder holder)
        {
            await Task.WhenAll(
                holder.ReadDb.Users.ToListAsync(),
                holder.WriteDb.Users.AnyAsync());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
