using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations.ConcurrentDbContextOperationsAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC046_ConcurrentDbContextOperations;

public sealed class ConcurrentDbContextOperationsTests
{
    internal const string EfMock = @"
	#nullable enable
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
        public Infrastructure.DatabaseFacade Database { get; } = new Infrastructure.DatabaseFacade();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public ValueTask<TEntity> FindAsync<TEntity>(params object[] keyValues) where TEntity : class => default;
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => new DbSet<TEntity>();
        public DbSet<TEntity> Set<TEntity>(string name) where TEntity : class => new DbSet<TEntity>();
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
        public ValueTask<TEntity> FindAsync(params object[] keyValues) => default;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<List<TEntity>> ToListAsync<TEntity>(
            this IQueryable<TEntity> source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<TEntity>());

        public static Task<bool> AnyAsync<TEntity>(
            this IQueryable<TEntity> source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public static Task<bool> ContainsAsync<TEntity>(
            this IQueryable<TEntity> source,
            TEntity item,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public static Task<TEntity> ElementAtAsync<TEntity>(
            this IQueryable<TEntity> source,
            int index,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(default(TEntity));

        public static Task<TEntity> ElementAtOrDefaultAsync<TEntity>(
            this IQueryable<TEntity> source,
            int index,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(default(TEntity));

        public static Task<Dictionary<TKey, TEntity>> ToDictionaryAsync<TEntity, TKey>(
            this IQueryable<TEntity> source,
            Func<TEntity, TKey> keySelector,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new Dictionary<TKey, TEntity>());

        public static Task LoadAsync<TEntity>(
            this IQueryable<TEntity> source,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public static Task<int> ExecuteUpdateAsync<TEntity>(
            this IQueryable<TEntity> source,
            object setters,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public static Task<int> ExecuteDeleteAsync<TEntity>(
            this IQueryable<TEntity> source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public static IAsyncEnumerable<TEntity> AsAsyncEnumerable<TEntity>(
            this IQueryable<TEntity> source) => null;
    }

    public static class RelationalQueryableExtensions
    {
        public static IQueryable<TEntity> FromSqlRaw<TEntity>(
            this DbSet<TEntity> source,
            string sql,
            params object[] parameters) where TEntity : class => source;
    }

    public static class RelationalDatabaseFacadeExtensions
    {
        public static Task<int> ExecuteSqlRawAsync(
            this Infrastructure.DatabaseFacade database,
            string sql,
            params object[] parameters) =>
            Task.FromResult(0);

        public static Task<int> ExecuteSqlRawAsync(
            this Infrastructure.DatabaseFacade database,
            string sql,
            CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public static Task<int> ExecuteSqlInterpolatedAsync(
            this Infrastructure.DatabaseFacade database,
            FormattableString sql,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}

namespace Microsoft.EntityFrameworkCore.Infrastructure
{
    public sealed class DatabaseFacade
    {
    }
}
";

    [Fact]
    public async Task TaskWhenAll_WithTwoQueriesOnSameContext_ShouldTriggerOnSecondOperation()
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
        public async Task Run(AppDbContext db)
        {
            await Task.WhenAll(
                {|#0:db.Users.ToListAsync()|},
                {|#1:db.Users.ToListAsync()|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TaskWhenAll_WithRequiredLocalAssignedByEarlierNamedArgument_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
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
        public async Task Run(AppDbContext db)
        {
            Func<User, int> selector = null;
            await Task.WhenAll(
                {|#0:db.Users.ToDictionaryAsync(
                    cancellationToken: (selector = _ => 0) == null
                        ? CancellationToken.None
                        : CancellationToken.None,
                    keySelector: selector)|},
                {|#1:db.Users.ToDictionaryAsync(
                    cancellationToken: (selector = _ => 1) == null
                        ? CancellationToken.None
                        : CancellationToken.None,
                    keySelector: selector)|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TaskWhenAll_WithNullableMethodGroupReceiverInLocalHelper_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Selector
    {
        public int Select(User user) => 0;
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db, Selector? selector)
        {
            Task<Dictionary<int, User>> Load(
                AppDbContext current,
                int ignored) =>
                current.Users.ToDictionaryAsync(selector.Select);

            await Task.WhenAll(Load(db, 0), Load(db, 1));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_WithDirectLocalFunctionReturns_ShouldTriggerOnlyForCapturedContext()
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
        public async Task CapturedContext(AppDbContext db)
        {
            Task<bool> Start() => db.Users.AnyAsync();

            await Task.WhenAll(
                {|#0:Start()|},
                {|#1:Start()|});
        }

        public async Task FreshContextPerCall()
        {
            Task<bool> Start() => new AppDbContext().Users.AnyAsync();

            await Task.WhenAll(
                Start(),
                Start());
        }

        public async Task ParameterizedDifferentContexts(
            AppDbContext first,
            AppDbContext second)
        {
            Task<bool> Start(AppDbContext current) => current.Users.AnyAsync();

            await Task.WhenAll(
                Start(first),
                Start(second));
        }

        public async Task HelperChain(AppDbContext db)
        {
            Task<bool> Start() => db.Users.AnyAsync();
            Task<bool> Wrapped() => Start();

            await Task.WhenAll(
                Wrapped(),
                Wrapped());
        }

        public async Task ReassignedCapturedParameter(AppDbContext db)
        {
            Task<bool> Start() => db.Users.AnyAsync();

            var first = Start();
            db = new AppDbContext();
            var second = Start();
            await Task.WhenAll(first, second);
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TaskWhenAll_WithDirectParameterizedLocalFunctionReturns_ShouldTriggerForSameContextOrCapture()
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
        public async Task Run(AppDbContext db)
        {
            Task<bool> Start(AppDbContext current) => current.Users.AnyAsync();

            await Task.WhenAll(
                {|#0:Start(db)|},
                {|#1:Start(db)|});
        }

        public async Task CapturedContext(AppDbContext db, AppDbContext ignored)
        {
            Task<bool> Start(AppDbContext current) => db.Users.AnyAsync();

            await Task.WhenAll(
                {|#2:Start(ignored)|},
                {|#3:Start(ignored)|});
        }
    }
}";

        var directParameter = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");
        var capturedContext = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithLocation(2)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, directParameter, capturedContext);
    }

    [Fact]
    public async Task TaskWhenAll_WithSafeNonContextParameterAndCapturedContext_ShouldTrigger()
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
        public async Task Run(AppDbContext db)
        {
            Task<User> Load(int index) => db.Users.ElementAtAsync(index);

            await Task.WhenAll(
                {|#0:Load(0)|},
                {|#1:Load(1)|});
        }

        public async Task UnusedParameter(AppDbContext db)
        {
            Task<bool> Load(int ignored) => db.Users.AnyAsync();

            await Task.WhenAll(
                {|#2:Load(0)|},
                {|#3:Load(1)|});
        }
    }
}";

        var queryParameter = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");
        var unusedParameter = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithLocation(2)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, queryParameter, unusedParameter);
    }

    [Fact]
    public async Task TaskWhenAll_WithTwoSafeParametersAndCapturedContext_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading;
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
        public async Task Run(AppDbContext db)
        {
            Task<User> Load(int index, CancellationToken token) =>
                db.Users.ElementAtAsync(index, token);

            await Task.WhenAll(
                {|#0:Load(0, CancellationToken.None)|},
                {|#1:Load(1, CancellationToken.None)|});
        }

        public async Task OptionalToken(AppDbContext db)
        {
            Task<User> Load(int index, CancellationToken token = default) =>
                db.Users.ElementAtAsync(index, token);

            await Task.WhenAll(
                {|#2:Load(0)|},
                {|#3:Load(1)|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");
        var optionalToken = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithLocation(2)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected, optionalToken);
    }

    [Fact]
    public async Task TaskWhenAll_WithTwoParameterDirectContextBinding_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
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
        public async Task ScalarArgument(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, int index) =>
                current.Users.ElementAtAsync(index);

            await Task.WhenAll(
                {|#0:Load(db, 0)|},
                {|#1:Load(db, 1)|});
        }

        public async Task ReorderedNamedArguments(AppDbContext db)
        {
            Task<User> Load(int index, AppDbContext current) =>
                current.Users.ElementAtAsync(index);

            await Task.WhenAll(
                {|#2:Load(current: db, index: 0)|},
                {|#3:Load(current: db, index: 1)|});
        }

        public async Task ForwardedToken(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, CancellationToken token) =>
                current.Users.ElementAtAsync(0, token);

            await Task.WhenAll(
                {|#4:Load(db, CancellationToken.None)|},
                {|#5:Load(db, CancellationToken.None)|});
        }

        public async Task OmittedOptionalCompanion(AppDbContext db)
        {
            Task<bool> Load(AppDbContext current, int ignored = 0) =>
                current.Users.AnyAsync();

            await Task.WhenAll(
                {|#6:Load(db)|},
                {|#7:Load(db)|});
        }

        public async Task DynamicRefWriteBeforeRequiredLocal(
            AppDbContext db,
            dynamic mutator)
        {
            var sql = """";
            mutator.Set(ref sql);
            Task<int> Execute(AppDbContext current, string command) =>
                current.Database.ExecuteSqlRawAsync(command);

            await Task.WhenAll(
                {|#8:Execute(db, sql)|},
                {|#9:Execute(db, sql)|});
        }

        public async Task BoundValidSetName(AppDbContext db)
        {
            Task<bool> Load(AppDbContext current, string name) =>
                current.Set<User>(name).AnyAsync();

            await Task.WhenAll(
                {|#10:Load(db, ""Users"")|},
                {|#11:Load(db, ""Users"")|});
        }

        public async Task DynamicByValueRead(
            AppDbContext db,
            dynamic observer)
        {
            var current = db;
            observer.Observe(current);

            await Task.WhenAll(
                {|#12:current.Users.AnyAsync()|},
                {|#13:current.Users.AnyAsync()|});
        }

        public async Task WritableInRefEscapeBeforeRequiredLocal(AppDbContext db)
        {
            var sql = """";
            System.Runtime.CompilerServices.Unsafe.AsRef(in sql) = ""SELECT 1"";
            Task<int> Execute(AppDbContext current, string command) =>
                current.Database.ExecuteSqlRawAsync(command);

            await Task.WhenAll(
                {|#14:Execute(db, sql)|},
                {|#15:Execute(db, sql)|});
        }

        public async Task CapturedRequiredLocalValidatedByCompanionArgument(
            AppDbContext db)
        {
            var sql = """";
            Task<int> Execute(AppDbContext current, bool ignored) =>
                current.Database.ExecuteSqlRawAsync(sql);

            await Task.WhenAll(
                {|#16:Execute(db, (sql = ""SELECT 1"") != null)|},
                {|#17:Execute(db, false)|});
        }

        public async Task UninvokedLambdaTokenReference(
            AppDbContext db)
        {
            Task<int> Save(AppDbContext current, CancellationToken token) =>
                current.SaveChangesAsync(
                    ((Func<bool>)(() => Observe(token))) == null
                        ? CancellationToken.None
                        : CancellationToken.None);

            var canceled = new CancellationToken(true);
            await Task.WhenAll(
                {|#18:Save(db, canceled)|},
                {|#19:Save(db, canceled)|});
        }

        public async Task UninvokedLambdaInvalidRequiredValue(
            AppDbContext db,
            AppDbContext other)
        {
            Task<int> Save(AppDbContext current, int ignored) =>
                current.SaveChangesAsync(
                    ((Func<bool>)(() => other.Set<User>("""").Any())) == null
                        ? CancellationToken.None
                        : CancellationToken.None);

            await Task.WhenAll(
                {|#20:Save(db, 0)|},
                {|#21:Save(db, 1)|});
        }

        public async Task ShortCircuitCompanionAssignmentDoesNotInvalidateValidRequiredLocal(
            AppDbContext db,
            bool flag)
        {
            var sql = ""SELECT 1"";
            Task<int> Execute(AppDbContext current, bool ignored) =>
                current.Database.ExecuteSqlRawAsync(sql);

            await Task.WhenAll(
                {|#22:Execute(db, flag && (sql = """") != null)|},
                {|#23:Execute(db, false)|});
        }

        public async Task TernaryCompanionAssignmentDoesNotInvalidateValidRequiredLocal(
            AppDbContext db,
            bool flag)
        {
            var sql = ""SELECT 1"";
            Task<int> Execute(AppDbContext current, bool ignored) =>
                current.Database.ExecuteSqlRawAsync(sql);

            await Task.WhenAll(
                {|#24:Execute(db, flag ? (sql = """") != null : false)|},
                {|#25:Execute(db, false)|});
        }

        private static bool Observe(CancellationToken token) =>
            token.IsCancellationRequested;
    }
}";

        var scalarArgument = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");
        var reorderedNamedArguments = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithLocation(2)
            .WithArguments("db");
        var forwardedToken = VerifyCS.Diagnostic()
            .WithLocation(5)
            .WithLocation(4)
            .WithArguments("db");
        var omittedOptionalCompanion = VerifyCS.Diagnostic()
            .WithLocation(7)
            .WithLocation(6)
            .WithArguments("db");
        var dynamicRefWrite = VerifyCS.Diagnostic()
            .WithLocation(9)
            .WithLocation(8)
            .WithArguments("db");
        var boundValidSetName = VerifyCS.Diagnostic()
            .WithLocation(11)
            .WithLocation(10)
            .WithArguments("db");
        var dynamicByValueRead = VerifyCS.Diagnostic()
            .WithLocation(13)
            .WithLocation(12)
            .WithArguments("db");
        var writableInRefEscape = VerifyCS.Diagnostic()
            .WithLocation(15)
            .WithLocation(14)
            .WithArguments("db");
        var validatedCapturedRequiredLocal = VerifyCS.Diagnostic()
            .WithLocation(17)
            .WithLocation(16)
            .WithArguments("db");
        var uninvokedLambdaTokenReference = VerifyCS.Diagnostic()
            .WithLocation(19)
            .WithLocation(18)
            .WithArguments("db");
        var uninvokedLambdaInvalidRequiredValue = VerifyCS.Diagnostic()
            .WithLocation(21)
            .WithLocation(20)
            .WithArguments("db");
        var shortCircuitDoesNotInvalidate = VerifyCS.Diagnostic()
            .WithLocation(23)
            .WithLocation(22)
            .WithArguments("db");
        var ternaryDoesNotInvalidate = VerifyCS.Diagnostic()
            .WithLocation(25)
            .WithLocation(24)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            scalarArgument,
            reorderedNamedArguments,
            forwardedToken,
            omittedOptionalCompanion,
            dynamicRefWrite,
            boundValidSetName,
            dynamicByValueRead,
            writableInRefEscape,
            validatedCapturedRequiredLocal,
            uninvokedLambdaTokenReference,
            uninvokedLambdaInvalidRequiredValue,
            shortCircuitDoesNotInvalidate,
            ternaryDoesNotInvalidate);
    }

    [Fact]
    public async Task TaskWhenAll_WithDirectContextHelperAndLiteralSelector_ShouldTrigger()
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
        public async Task Run(AppDbContext db)
        {
            Task<System.Collections.Generic.Dictionary<int, User>> Load(
                AppDbContext current) =>
                current.Users.ToDictionaryAsync(_ => 0);

            await Task.WhenAll({|#0:Load(db)|}, {|#1:Load(db)|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TaskWhenAll_WithUnprovenTwoParameterDirectContextBinding_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
        public int ThrowingIndex => throw new System.InvalidOperationException();
    }

    public sealed class OtherDbContext : DbContext { }

    public sealed class Program
    {
        public async Task DifferentContexts(AppDbContext db, AppDbContext other)
        {
            Task<User> Load(AppDbContext current, int index) =>
                current.Users.ElementAtAsync(index);

            await Task.WhenAll(Load(db, 0), Load(other, 1));
        }

        public async Task ThrowingCallArgument(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, int index) =>
                current.Users.ElementAtAsync(index);

            await Task.WhenAll(Load(db, GetIndex()), Load(db, GetIndex()));
        }

        public async Task ThrowingParameterUse(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, int divisor) =>
                current.Users.ElementAtAsync(10 / divisor);

            await Task.WhenAll(Load(db, 1), Load(db, 2));
        }

        public async Task DefinitelyCancelledToken(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, CancellationToken token) =>
                current.Users.ElementAtAsync(0, token);

            var canceled = new CancellationToken(true);
            await Task.WhenAll(Load(db, canceled), Load(db, canceled));
        }

        public async Task ReassignedContext(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, int index) =>
                current.Users.ElementAtAsync(index);

            var first = Load(db, 0);
            db = new AppDbContext();
            var second = Load(db, 1);
            await Task.WhenAll(first, second);
        }

        public async Task AmbiguousContextParameters(
            AppDbContext db,
            AppDbContext other)
        {
            Task<bool> Load(AppDbContext current, AppDbContext ignored) =>
                current.Users.AnyAsync();

            await Task.WhenAll(Load(db, other), Load(db, other));
        }

        public async Task ThrowingContextParameterUse(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, int ignored) =>
                current.Users.ElementAtAsync(current.ThrowingIndex);

            await Task.WhenAll(Load(db, 0), Load(db, 0));
        }

        public async Task InvalidRequiredArgument(AppDbContext db)
        {
            Task<int> Execute(AppDbContext current, string sql) =>
                current.Database.ExecuteSqlRawAsync(sql);

            await Task.WhenAll(Execute(db, """"), Execute(db, """"));
        }

        public async Task WrappedCancelledToken(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, CancellationToken token) =>
                current.Users.ElementAtAsync(
                    0,
                    true ? token : CancellationToken.None);

            var canceled = new CancellationToken(true);
            await Task.WhenAll(Load(db, canceled), Load(db, canceled));
        }

        public async Task EarlierArgumentReassignsContext(
            AppDbContext db,
            AppDbContext other)
        {
            Task<bool> Load(bool ignored, AppDbContext current) =>
                current.Users.AnyAsync();

            var first = Load(ignored: false, current: db);
            var second = Load(
                ignored: (db = other) != null,
                current: db);
            await Task.WhenAll(first, second);
        }

        public async Task TransformedInvalidRequiredArgument(AppDbContext db)
        {
            Task<int> Execute(AppDbContext current, string sql) =>
                current.Database.ExecuteSqlRawAsync(sql ?? """");

            await Task.WhenAll(Execute(db, null), Execute(db, null));
        }

        public async Task TransformedInvalidBoundRequiredArgument(AppDbContext db)
        {
            Task<int> Execute(AppDbContext current, string command) =>
                current.Database.ExecuteSqlRawAsync(command);

            string sql = null;
            await Task.WhenAll(
                Execute(db, sql ?? """"),
                Execute(db, sql ?? """"));
        }

        public async Task TransformedCapturedInvalidRequiredArgument(AppDbContext db)
        {
            string sql = null;
            Task<int> Execute(AppDbContext current, int ignored) =>
                current.Database.ExecuteSqlRawAsync(sql ?? """");

            await Task.WhenAll(Execute(db, 0), Execute(db, 1));
        }

        public async Task EmptyFindKeys(AppDbContext db)
        {
            ValueTask<User> Load(AppDbContext current, int ignored) =>
                current.FindAsync<User>();

            var first = Load(db, 0);
            var second = Load(db, 1);
            await first;
            await second;
        }

        public async Task InvalidNestedSetName(AppDbContext db)
        {
            Task<bool> Load(AppDbContext current, string name) =>
                current.Set<User>(name).AnyAsync();

            await Task.WhenAll(Load(db, """"), Load(db, """"));
        }

        public async Task DynamicContextReplacement(
            AppDbContext db,
            AppDbContext other,
            dynamic mutator)
        {
            Task<User> Load(AppDbContext current, int index) =>
                current.Users.ElementAtAsync(index);

            var current = db;
            var first = Load(current, 0);
            mutator.Replace(ref current, other);
            var second = Load(current, 1);
            await Task.WhenAll(first, second);
        }

        public async Task CapturedThrowingTerminalArgument(AppDbContext db)
        {
            Task<User> Load(AppDbContext current, int ignored) =>
                current.Users.ElementAtAsync(ThrowingIndex);

            await Task.WhenAll(Load(db, 0), Load(db, 1));
        }

        public async Task CapturedThrowingSetName(AppDbContext db)
        {
            Task<bool> Load(AppDbContext current, int ignored) =>
                current.Set<User>(ThrowingName).AnyAsync();

            await Task.WhenAll(Load(db, 0), Load(db, 1));
        }

        public async Task StableInvalidRequiredArgument(AppDbContext db)
        {
            Task<int> Execute(AppDbContext current, string sql) =>
                current.Database.ExecuteSqlRawAsync(sql);

            var sql = """";
            await Task.WhenAll(Execute(db, sql), Execute(db, sql));
        }

        public async Task StringEmptyRequiredArgument(AppDbContext db)
        {
            Task<int> Execute(AppDbContext current, string sql) =>
                current.Database.ExecuteSqlRawAsync(sql);

            await Task.WhenAll(
                Execute(db, string.Empty),
                Execute(db, string.Empty));
        }

        public async Task AssignedInvalidRequiredArgument(AppDbContext db)
        {
            Task<int> Execute(AppDbContext current, string sql) =>
                current.Database.ExecuteSqlRawAsync(sql);

            var sql = ""valid"";
            await Task.WhenAll(
                Execute(db, sql = """"),
                Execute(db, sql = """"));
        }

        public async Task ThrowingContextDowncast(OtherDbContext db)
        {
            Task<bool> Load(DbContext current, int ignored) =>
                ((AppDbContext)current).Users.AnyAsync();

            await Task.WhenAll(Load(db, 0), Load(db, 1));
        }

        public async Task ThrowingRootContextDowncast(OtherDbContext db)
        {
            Task<int> Save(DbContext current, int ignored) =>
                ((AppDbContext)current).SaveChangesAsync();

            await Task.WhenAll(Save(db, 0), Save(db, 1));
        }

        public async Task CapturedStableInvalidRequiredArgument(AppDbContext db)
        {
            var sql = """";
            Task<int> Execute(AppDbContext current, int ignored) =>
                current.Database.ExecuteSqlRawAsync(sql);

            await Task.WhenAll(Execute(db, 0), Execute(db, 1));
        }

        public async Task CapturedRequiredLocalInvalidatedByCompanionArgument(
            AppDbContext db)
        {
            var sql = ""SELECT 1"";
            Task<int> Execute(AppDbContext current, bool ignored) =>
                current.Database.ExecuteSqlRawAsync(sql);

            await Task.WhenAll(
                Execute(db, (sql = """") != null),
                Execute(db, false));
        }

        public async Task CapturedRequiredLocalInvalidatedByEarlierHelperArgument(
            AppDbContext db)
        {
            var sql = ""SELECT 1"";
            Task<int> Execute(AppDbContext current, int ignored) =>
                current.Database.ExecuteSqlRawAsync(
                    cancellationToken: (sql = """") == null
                        ? CancellationToken.None
                        : CancellationToken.None,
                    sql: sql);

            await Task.WhenAll(Execute(db, 0), Execute(db, 1));
        }

        public async Task CapturedRequiredLocalInvalidatedByDeconstructionArgument(
            AppDbContext db)
        {
            var sql = ""SELECT 1"";
            Task<int> Execute(AppDbContext current, (string, int) ignored) =>
                current.Database.ExecuteSqlRawAsync(sql);

            await Task.WhenAll(
                Execute(db, (sql, _) = ("""", 0)),
                Execute(db, (sql, _) = ("""", 0)));
        }

        public async Task CapturedEmptyCollectionExpressionFindKeys(
            AppDbContext db)
        {
            object[] keys = [];
            ValueTask<User> Load(AppDbContext current, int ignored) =>
                current.FindAsync<User>(keys);

            var first = Load(db, 0);
            var second = Load(db, 1);
            await first;
            await second;
        }

        public async Task CapturedDefinitelyCancelledToken(AppDbContext db)
        {
            var canceled = new CancellationToken(true);
            Task<bool> Load(AppDbContext current, int ignored) =>
                current.Users.AnyAsync(canceled);

            await Task.WhenAll(Load(db, 0), Load(db, 1));
        }

        public async Task CapturedInvalidRequiredAlias(AppDbContext db)
        {
            var source = """";
            var sql = source;
            source = ""SELECT 1"";
            Task<int> Execute(AppDbContext current, int ignored) =>
                current.Database.ExecuteSqlRawAsync(sql);

            await Task.WhenAll(Execute(db, 0), Execute(db, 1));
        }

        public async Task DynamicContextParameterReplacement(
            AppDbContext db,
            AppDbContext other,
            dynamic mutator)
        {
            Task<User> Load(AppDbContext current, int index) =>
                current.Users.ElementAtAsync(index);

            var first = Load(db, 0);
            mutator.Replace(ref db, other);
            var second = Load(db, 1);
            await Task.WhenAll(first, second);
        }

        public async Task ShortCircuitCompanionAssignmentDoesNotValidateInvalidRequiredLocal(
            AppDbContext db,
            bool flag)
        {
            var sql = """";
            Task<int> Execute(AppDbContext current, bool ignored) =>
                current.Database.ExecuteSqlRawAsync(sql);

            await Task.WhenAll(
                Execute(db, flag && (sql = ""SELECT 1"") != null),
                Execute(db, false));
        }

        public async Task TernaryCompanionAssignmentDoesNotValidateInvalidRequiredLocal(
            AppDbContext db,
            bool flag)
        {
            var sql = """";
            Task<int> Execute(AppDbContext current, bool ignored) =>
                current.Database.ExecuteSqlRawAsync(sql);

            await Task.WhenAll(
                Execute(db, flag ? (sql = ""SELECT 1"") != null : false),
                Execute(db, false));
        }

        private static int GetIndex() => 0;
        private static int ThrowingIndex => throw new System.InvalidOperationException();
        private static string ThrowingName => throw new System.InvalidOperationException();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_WithUnprovenTwoParameterLocalFunctionCalls_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading;
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
        public async Task ThrowingCallArgument(AppDbContext db)
        {
            Task<User> Load(int index, CancellationToken token) =>
                db.Users.ElementAtAsync(index, token);

            await Task.WhenAll(
                Load(GetIndex(), CancellationToken.None),
                Load(GetIndex(), CancellationToken.None));
        }

        public async Task ThrowingParameterUse(AppDbContext db)
        {
            Task<User> Load(int divisor, CancellationToken token) =>
                db.Users.ElementAtAsync(10 / divisor, token);

            await Task.WhenAll(
                Load(1, CancellationToken.None),
                Load(2, CancellationToken.None));
        }

        public async Task ThrowingSecondCallArgument(AppDbContext db)
        {
            Task<User> Load(CancellationToken token, int index) =>
                db.Users.ElementAtAsync(index, token);

            await Task.WhenAll(
                Load(CancellationToken.None, GetIndex()),
                Load(CancellationToken.None, GetIndex()));
        }

        public async Task ThrowingSecondParameterUse(AppDbContext db)
        {
            Task<User> Load(CancellationToken token, int divisor) =>
                db.Users.ElementAtAsync(10 / divisor, token);

            await Task.WhenAll(
                Load(CancellationToken.None, 1),
                Load(CancellationToken.None, 2));
        }

        public async Task DefinitelyCancelledToken(AppDbContext db)
        {
            Task<User> Load(int index, CancellationToken token) =>
                db.Users.ElementAtAsync(index, token);

            var canceled = new CancellationToken(true);
            await Task.WhenAll(Load(0, canceled), Load(1, canceled));
        }

        public async Task ReassignedCapture(AppDbContext db)
        {
            Task<User> Load(int index, CancellationToken token) =>
                db.Users.ElementAtAsync(index, token);

            var first = Load(0, CancellationToken.None);
            db = new AppDbContext();
            var second = Load(1, CancellationToken.None);
            await Task.WhenAll(first, second);
        }

        public async Task FreshContext(AppDbContext db)
        {
            Task<User> Load(int index, CancellationToken token) =>
                new AppDbContext().Users.ElementAtAsync(index, token);

            await Task.WhenAll(
                Load(0, CancellationToken.None),
                Load(1, CancellationToken.None));
        }

        public async Task ThreeParameters(AppDbContext db)
        {
            Task<User> Load(int index, CancellationToken token, bool ignored) =>
                db.Users.ElementAtAsync(index, token);

            await Task.WhenAll(
                Load(0, CancellationToken.None, true),
                Load(1, CancellationToken.None, true));
        }

        private static int GetIndex() => 0;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_WithUnprovenOneParameterLocalFunctionCalls_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
        public int Index { get; }
    }

    public sealed class ContextHolder
    {
        public AppDbContext Context { get; } = new AppDbContext();
    }

    public sealed class Program
    {
        public async Task FreshContext(AppDbContext db)
        {
            Task<User> Load(int index) =>
                new AppDbContext().Users.ElementAtAsync(index);

            await Task.WhenAll(Load(0), Load(1));
        }

        public async Task ReassignedCapture(AppDbContext db)
        {
            Task<User> Load(int index) => db.Users.ElementAtAsync(index);

            var first = Load(0);
            db = new AppDbContext();
            var second = Load(1);
            await Task.WhenAll(first, second);
        }

        public async Task ThrowingArguments(AppDbContext db)
        {
            Task<User> Load(int index) => db.Users.ElementAtAsync(index);

            await Task.WhenAll(Load(GetIndex()), Load(GetIndex()));
        }

        public async Task ThrowingParameterUse(AppDbContext db)
        {
            Task<User> Load(int divisor) =>
                db.Users.ElementAtAsync(10 / divisor);

            await Task.WhenAll(Load(0), Load(1));
        }

        public async Task ThrowingContextProperty(ContextHolder holder)
        {
            Task<bool> Load(AppDbContext current) => current.Users.AnyAsync();

            await Task.WhenAll(
                Load(holder.Context),
                Load(holder.Context));
        }

        public async Task ThrowingUnusedContextArgument(AppDbContext db)
        {
            Task<bool> Load(AppDbContext ignored) => db.Users.AnyAsync();

            await Task.WhenAll(Load(CreateContext()), Load(CreateContext()));
        }

        public async Task ThrowingContextParameterUse(
            AppDbContext db,
            AppDbContext other)
        {
            Task<User> Load(AppDbContext current) =>
                db.Users.ElementAtAsync(current.Index);

            await Task.WhenAll(Load(other), Load(other));
        }

        public async Task CapturedContextInvalidNestedSetName(AppDbContext db)
        {
            Task<bool> Load(string name) => db.Set<User>(name).AnyAsync();

            await Task.WhenAll(Load(""""), Load(""""));
        }

        public async Task CapturedContextThrowingNestedSetName(AppDbContext db)
        {
            Task<bool> Load(string name) =>
                db.Set<User>(ThrowName(name)).AnyAsync();

            await Task.WhenAll(Load(""Users""), Load(""Users""));
        }

        private static int GetIndex() => 0;
        private static AppDbContext CreateContext() => new AppDbContext();
        private static string ThrowName(string name) =>
            throw new System.InvalidOperationException();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskWhenAll_WithParameterizedLocalFunctionOutsideDirectContextBinding_ShouldNotTrigger()
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
        public async Task FreshInsteadOfParameter(AppDbContext db)
        {
            Task<bool> Start(AppDbContext current) =>
                new AppDbContext().Users.AnyAsync();

            await Task.WhenAll(
                Start(db),
                Start(db));
        }

        public async Task ReassignedArgument(AppDbContext db)
        {
            Task<bool> Start(AppDbContext current) => current.Users.AnyAsync();

            var first = Start(db);
            db = new AppDbContext();
            var second = Start(db);
            await Task.WhenAll(first, second);
        }

        public async Task ReassignedCapturedContext(
            AppDbContext db,
            AppDbContext ignored)
        {
            Task<bool> Start(AppDbContext current) => db.Users.AnyAsync();

            var first = Start(ignored);
            db = new AppDbContext();
            var second = Start(ignored);
            await Task.WhenAll(first, second);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskLocals_WithSameContext_ShouldTriggerOnSecondOperation()
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
        public async Task Run(AppDbContext db)
        {
            var first = {|#0:db.Users.ToListAsync()|};
            var second = {|#1:db.Users.ToListAsync()|};
            await Task.WhenAll(first, second);
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DiscardedTask_ThenSameContextOperation_ShouldTrigger()
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
        public async Task Run(AppDbContext db)
        {
            _ = {|#0:db.Users.ToListAsync()|};
            await {|#1:db.Users.AnyAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task UnawaitedQuery_ThenAwaitedSaveChanges_ShouldTrigger()
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
        public async Task Run(AppDbContext db)
        {
            var query = {|#0:db.Users.AnyAsync()|};
            await {|#1:db.SaveChangesAsync()|};
            await query;
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DbContextSet_WithConcurrentQueries_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public async Task Run(DbContext db)
        {
            await Task.WhenAll(
                {|#0:db.Set<User>().AnyAsync()|},
                {|#1:db.Set<User>().ToListAsync()|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DbContextSet_WithUnprovenName_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public async Task NullName(DbContext db)
        {
            await Task.WhenAll(
                db.Set<User>((string)null).AnyAsync(),
                db.Set<User>((string)null).ToListAsync());
        }

        public async Task BlankName(DbContext db)
        {
            await Task.WhenAll(
                db.Set<User>(""   "").AnyAsync(),
                db.Set<User>(""   "").ToListAsync());
        }

    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DbContextSet_WithUnprovenName_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public async Task Run(DbContext db, string name)
        {
            await Task.WhenAll(
                {|#0:db.Set<User>(name).AnyAsync()|},
                {|#1:db.Set<User>(name).ToListAsync()|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DbContextSet_WithProvenName_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public async Task Run(DbContext db)
        {
            await Task.WhenAll(
                {|#0:db.Set<User>(""Users"").AnyAsync()|},
                {|#1:db.Set<User>(""Users"").ToListAsync()|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DbContextSet_WithUnprovenNameInUnawaitedStatements_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var first = db.Set<User>((string)null).AnyAsync();
            var second = db.Set<User>((string)null).ToListAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DbContextSet_WithMixedNameValidity_ShouldStillReportValidPair()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public async Task Run(DbContext db)
        {
            await Task.WhenAll(
                {|#0:db.Set<User>(""Users"").AnyAsync()|},
                db.Set<User>((string)null).ToListAsync(),
                {|#1:db.Set<User>(""Users"").ContainsAsync(null)|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DbContextSet_WithTrailingUnprovenName_ShouldStillReportValidPair()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public async Task Run(DbContext db)
        {
            await Task.WhenAll(
                {|#0:db.Set<User>(""Users"").AnyAsync()|},
                {|#1:db.Set<User>(""Users"").ToListAsync()|},
                db.Set<User>((string)null).ContainsAsync(null));
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DbContextSet_WithProvenLocalName_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public async Task Run(DbContext db)
        {
            var name = ""Users"";
            await Task.WhenAll(
                {|#0:db.Set<User>(name).AnyAsync()|},
                {|#1:db.Set<User>(name).ToListAsync()|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DbContextSet_WithUnprovenNameHoistedToLocal_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public async Task Run(DbContext db)
        {
            var set = db.Set<User>((string)null);
            await Task.WhenAll(
                set.AnyAsync(),
                set.ToListAsync());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DbContextSet_WithNoNameArgument_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }

    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var first = {|#0:db.Set<User>().AnyAsync()|};
            var second = {|#1:db.Set<User>().ToListAsync()|};
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NonLoopOperations_WithUnprovenArgumentsFromParameters_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading;
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
        public async Task SqlFromParameter(AppDbContext db, string sql)
        {
            await Task.WhenAll(
                {|#0:db.Database.ExecuteSqlRawAsync(sql)|},
                {|#1:db.Database.ExecuteSqlRawAsync(sql)|});
        }

        public async Task TokenFromParameter(AppDbContext db, CancellationToken ct)
        {
            await Task.WhenAll(
                {|#2:db.Users.AnyAsync(ct)|},
                {|#3:db.Users.ToListAsync(ct)|});
        }

    }
}";

        var sqlFromParameter = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");
        var tokenFromParameter = VerifyCS.Diagnostic()
            .WithLocation(3)
            .WithLocation(2)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, sqlFromParameter, tokenFromParameter);
    }

    [Fact]
    public async Task NonLoopOperations_WithValidRequiredArguments_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
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
        public async Task RawSql(AppDbContext db)
        {
            await Task.WhenAll(
                {|#0:db.Database.ExecuteSqlRawAsync(""SELECT 1"")|},
                {|#1:db.Database.ExecuteSqlRawAsync(""SELECT 2"")|});
        }

        public async Task QuerySql(AppDbContext db)
        {
            await Task.WhenAll(
                {|#2:db.Users.FromSqlRaw(""SELECT 1"").AnyAsync()|},
                {|#3:db.Users.FromSqlRaw(""SELECT 2"").ToListAsync()|});
        }

        public void FindKeys(AppDbContext db)
        {
            var first = {|#4:db.FindAsync<User>(1)|};
            var second = {|#5:db.FindAsync<User>(2)|};
        }

        public async Task NonCancelledToken(AppDbContext db)
        {
            var token = new System.Threading.CancellationToken(false);
            await Task.WhenAll(
                {|#6:db.Users.AnyAsync(token)|},
                {|#7:db.Users.ToListAsync(token)|});
        }
    }
}";

        var rawSql = VerifyCS.Diagnostic().WithLocation(1).WithLocation(0).WithArguments("db");
        var querySql = VerifyCS.Diagnostic().WithLocation(3).WithLocation(2).WithArguments("db");
        var findKeys = VerifyCS.Diagnostic().WithLocation(5).WithLocation(4).WithArguments("db");
        var token = VerifyCS.Diagnostic().WithLocation(7).WithLocation(6).WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, rawSql, querySql, findKeys, token);
    }

    [Fact]
    public async Task NonLoopOperations_WithInvalidRequiredArguments_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
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
        public async Task NullRawSql(AppDbContext db)
        {
            await Task.WhenAll(
                db.Database.ExecuteSqlRawAsync((string)null),
                db.Database.ExecuteSqlRawAsync((string)null));
        }

        public async Task EmptyQuerySql(AppDbContext db)
        {
            await Task.WhenAll(
                db.Users.FromSqlRaw("""").AnyAsync(),
                db.Users.FromSqlRaw("""").ToListAsync());
        }

        public async Task NullFindKeys(AppDbContext db)
        {
            var first = db.FindAsync<User>((object[])null);
            var second = db.FindAsync<User>((object[])null);
        }

        public async Task DefinitelyCancelled(AppDbContext db)
        {
            var canceled = new System.Threading.CancellationToken(true);
            await Task.WhenAll(
                db.Users.AnyAsync(canceled),
                db.Users.ToListAsync(canceled));
        }

        public async Task WhitespaceQuerySql(AppDbContext db)
        {
            await Task.WhenAll(
                db.Users.FromSqlRaw(""   "").AnyAsync(),
                db.Users.FromSqlRaw(""   "").ToListAsync());
        }

        public async Task NullQuerySql(AppDbContext db)
        {
            await Task.WhenAll(
                db.Users.FromSqlRaw((string)null).AnyAsync(),
                db.Users.FromSqlRaw((string)null).ToListAsync());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StaticEfExtensionSyntax_WithSameContext_ShouldTrigger()
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
        public async Task Run(AppDbContext db)
        {
            await Task.WhenAll(
                {|#0:EntityFrameworkQueryableExtensions.ToListAsync(db.Users)|},
                {|#1:EntityFrameworkQueryableExtensions.AnyAsync(db.Users)|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ReorderedNamedStaticEfExtensionSyntax_ShouldTrigger()
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
        public async Task Run(AppDbContext db)
        {
            await Task.WhenAll(
                {|#0:EntityFrameworkQueryableExtensions.ToListAsync(cancellationToken: default, source: db.Users)|},
                {|#1:EntityFrameworkQueryableExtensions.AnyAsync(cancellationToken: default, source: db.Users)|});
        }
    }
}";

        var expected = VerifyCS.Diagnostic()
            .WithLocation(1)
            .WithLocation(0)
            .WithArguments("db");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task SequentialAwaits_OnSameContext_ShouldNotTrigger()
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
        public async Task Run(AppDbContext db)
        {
            await db.Users.ToListAsync();
            await db.Users.AnyAsync();
            await db.SaveChangesAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AwaitedTaskLocal_BeforeSecondOperation_ShouldNotTrigger()
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
        public async Task Run(AppDbContext db)
        {
            var first = db.Users.ToListAsync();
            await first.ConfigureAwait(false);
            await db.Users.AnyAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DifferentContexts_ShouldNotTrigger()
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
        public async Task Run(AppDbContext first, AppDbContext second)
        {
            await Task.WhenAll(
                first.Users.ToListAsync(),
                second.Users.ToListAsync());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task QueryConstructionAndAsAsyncEnumerable_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public bool Active { get; set; } }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public sealed class Program
    {
        public Task Run(AppDbContext db)
        {
            var query = db.Users.Where(user => user.Active);
            var stream = query.AsAsyncEnumerable();
            return Task.CompletedTask;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CustomAsyncLookalike_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;" + EfMock + @"
namespace TestApp
{
    public sealed class User { }
    public sealed class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; } = new DbSet<User>();
    }

    public static class CustomExtensions
    {
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) =>
            Task.FromResult(new List<T>());
    }

    public sealed class Program
    {
        public async Task Run(AppDbContext db)
        {
            await Task.WhenAll(
                CustomExtensions.ToListAsync(db.Users),
                CustomExtensions.ToListAsync(db.Users));
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
