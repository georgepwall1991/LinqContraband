using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC039_NestedSaveChanges.NestedSaveChangesAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC039_NestedSaveChanges;

public class NestedSaveChangesTests
{
    private const string EFCoreMock = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public void Dispose() { }
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
        public DatabaseFacade Database => new DatabaseFacade();
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => new DbSet<TEntity>();
    }

    public class DatabaseFacade
    {
        public IDbContextTransaction BeginTransaction() => new DbContextTransaction();
        public Task<IDbContextTransaction> BeginTransactionAsync() => Task.FromResult<IDbContextTransaction>(new DbContextTransaction());
    }

    public interface IDbContextTransaction : IDisposable, IAsyncDisposable
    {
        void Commit();
        Task CommitAsync();
        void Rollback();
        Task RollbackAsync();
        void CreateSavepoint(string name);
        Task CreateSavepointAsync(string name);
        void ReleaseSavepoint(string name);
        Task ReleaseSavepointAsync(string name);
        void RollbackToSavepoint(string name);
        Task RollbackToSavepointAsync(string name);
    }

    public class DbContextTransaction : IDbContextTransaction
    {
        public void Dispose() { }
        public System.Threading.Tasks.ValueTask DisposeAsync() => default;
        public void Commit() { }
        public Task CommitAsync() => Task.CompletedTask;
        public void Rollback() { }
        public Task RollbackAsync() => Task.CompletedTask;
        public void CreateSavepoint(string name) { }
        public Task CreateSavepointAsync(string name) => Task.CompletedTask;
        public void ReleaseSavepoint(string name) { }
        public Task ReleaseSavepointAsync(string name) => Task.CompletedTask;
        public void RollbackToSavepoint(string name) { }
        public Task RollbackToSavepointAsync(string name) => Task.CompletedTask;
    }

    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public System.Collections.Generic.IEnumerator<TEntity> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<TSource> AsNoTracking<TSource>(this IQueryable<TSource> source) => source;
        public static Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source) => Task.FromResult(new List<TSource>());
    }
}
";

    private const string Types = @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<User> Users { get; set; }
    }
}
";

    [Fact]
    public async Task TwoSaveChangesCallsOnSameContext_Triggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db = new TestApp.AppDbContext();
        db.SaveChanges();
        {|LC039:db.SaveChanges()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveChangesAndSaveChangesAsyncOnSameContext_Triggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    async Task Run()
    {
        var db = new TestApp.AppDbContext();
        db.SaveChanges();
        await {|LC039:db.SaveChangesAsync()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TransactionBoundaryBetweenSaves_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db = new TestApp.AppDbContext();
        db.SaveChanges();
        using var tx = db.Database.BeginTransaction();
        db.SaveChanges();
        tx.Commit();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RepeatedSaveChangesInsideTransactionUsingBlock_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db = new TestApp.AppDbContext();
        using (var tx = db.Database.BeginTransaction())
        {
            db.SaveChanges();
            db.SaveChanges();
            tx.Commit();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RepeatedSaveChangesInsideUnrelatedUsingBlock_Triggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db = new TestApp.AppDbContext();
        using (db)
        {
            db.SaveChanges();
            {|LC039:db.SaveChanges()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RepeatedSaveChangesInsideCustomBeginTransactionUsingBlock_Triggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db = new TestApp.AppDbContext();
        using (BeginTransaction())
        {
            db.SaveChanges();
            {|LC039:db.SaveChanges()|};
        }
    }

    FakeTransaction BeginTransaction() => new FakeTransaction();

    private sealed class FakeTransaction : System.IDisposable
    {
        public void Dispose()
        {
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UnrelatedCommitBetweenSaves_DoesNotSuppressDiagnostic()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db = new TestApp.AppDbContext();
        db.SaveChanges();
        Commit();
        {|LC039:db.SaveChanges()|};
    }

    void Commit()
    {
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IfElseSaveChangesBranches_DoNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool saveFirst)
    {
        var db = new TestApp.AppDbContext();
        if (saveFirst)
        {
            db.SaveChanges();
        }
        else
        {
            db.SaveChanges();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ElseIfSaveChangesBranches_DoNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    async Task Run(bool first, bool second)
    {
        var db = new TestApp.AppDbContext();
        if (first)
        {
            await db.SaveChangesAsync();
        }
        else if (second)
        {
            await db.SaveChangesAsync();
        }
        else
        {
            await db.SaveChangesAsync();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SwitchCaseSaveChangesBranches_DoNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(int state)
    {
        var db = new TestApp.AppDbContext();
        switch (state)
        {
            case 1:
                db.SaveChanges();
                break;
            case 2:
                db.SaveChanges();
                break;
            default:
                db.SaveChanges();
                break;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SameSwitchCaseRepeatedSaveChanges_Triggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(int state)
    {
        var db = new TestApp.AppDbContext();
        switch (state)
        {
            case 1:
                db.SaveChanges();
                {|LC039:db.SaveChanges()|};
                break;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IndependentIfStatements_CanTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool first, bool second)
    {
        var db = new TestApp.AppDbContext();
        if (first)
        {
            db.SaveChanges();
        }

        if (second)
        {
            {|LC039:db.SaveChanges()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SeparateContexts_DoNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db1 = new TestApp.AppDbContext();
        var db2 = new TestApp.AppDbContext();
        db1.SaveChanges();
        db2.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedLocalFunction_SaveChanges_UsesSeparateRoot()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db = new TestApp.AppDbContext();
        db.SaveChanges();

        void Inner()
        {
            db.SaveChanges();
        }

        Inner();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RepeatedSavesInsideUsingDeclarationTransaction_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db = new TestApp.AppDbContext();
        using var tx = db.Database.BeginTransaction();
        db.SaveChanges();
        db.SaveChanges();
        tx.Commit();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RepeatedSavesInsideAwaitUsingDeclarationTransaction_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    async Task Run()
    {
        var db = new TestApp.AppDbContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RepeatedSavesAfterUsingDeclarationOfNonTransaction_Triggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db = new TestApp.AppDbContext();
        using var stream = new System.IO.MemoryStream();
        db.SaveChanges();
        {|LC039:db.SaveChanges()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RepeatedSavesInsideUsingDeclarationTransactionWithinNestedBlock_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag)
    {
        var db = new TestApp.AppDbContext();
        using var tx = db.Database.BeginTransaction();
        if (flag)
        {
            db.SaveChanges();
            db.SaveChanges();
        }
        tx.Commit();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UsingDeclarationOfTransactionAfterFirstSave_DoesNotSuppress()
    {
        // Sanity guardrail: a using-declaration that comes AFTER the first save
        // must not retroactively cover saves that preceded it. The existing
        // boundary-between-positions check already handles this (BeginTransaction
        // is between the two saves), so this should pass and stay quiet.
        var test = EFCoreMock + Types + @"

class Program
{
    void Run()
    {
        var db = new TestApp.AppDbContext();
        db.SaveChanges();
        using var tx = db.Database.BeginTransaction();
        db.SaveChanges();
        tx.Commit();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
