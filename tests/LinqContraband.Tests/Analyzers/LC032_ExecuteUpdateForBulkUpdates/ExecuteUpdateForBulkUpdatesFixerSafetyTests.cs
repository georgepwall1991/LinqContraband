using Microsoft.CodeAnalysis;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates.ExecuteUpdateForBulkUpdatesAnalyzer,
    LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates.ExecuteUpdateForBulkUpdatesFixer>;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates.ExecuteUpdateForBulkUpdatesAnalyzer,
    LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates.ExecuteUpdateForBulkUpdatesFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

public partial class ExecuteUpdateForBulkUpdatesFixerTests
{
    [Fact]
    public async Task Fixer_DuplicateProperty_LaterReadsEarlierWrite_DoesNotRegister()
    {
        // The second write reads the first write's in-memory result ("AB"), but ExecuteUpdate
        // would evaluate `user.Name + "B"` against the original column value. Decline.
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Name = ""A"";
            user.Name = user.Name + ""B"";
        }|}
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_RhsReadsEarlierWrittenProperty_DoesNotRegister()
    {
        // user.LoginCount reads a value written earlier in the same iteration; the set-based
        // rewrite would read the original column instead. Decline.
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Id = 0;
            user.LoginCount = user.Id;
        }|}
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_AddsEntityFrameworkUsing_WhenAbsent()
    {
        // The loop compiles without importing Microsoft.EntityFrameworkCore (DbContext/DbSet
        // members + System.Linq), but the generated ExecuteUpdate extension needs the import.
        const string usingsNoEf = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestApp;
";

        var test = usingsNoEf + EFCoreMockWithExecuteUpdate + TestTypes + @"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Name = ""Archived"";
        }|}
        db.SaveChanges();
    }
}";

        const string usingsWithEf = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestApp;
using Microsoft.EntityFrameworkCore;
";

        var fixedCode = usingsWithEf + EFCoreMockWithExecuteUpdate + TestTypes + @"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        " + WarningComment + @"
        db.Users.Where(u => u.IsActive).ExecuteUpdate(setters => setters.SetProperty(user => user.Name, user => ""Archived""));
        db.SaveChanges();
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_SaveChangesResultReturned_DoesNotRegister()
    {
        // The trailing SaveChanges row count is returned; the rewrite would leave a SaveChanges
        // that now returns 0, silently changing the observed value. Decline.
        var test = WithExecuteUpdate(@"
class Program
{
    int Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Name = ""Archived"";
        }|}
        return db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_SaveChangesResultAssigned_DoesNotRegister()
    {
        var test = WithExecuteUpdate(@"
class Program
{
    int Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Name = ""Archived"";
        }|}
        var changed = db.SaveChanges();
        return changed;
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_TrailingSaveChangesViaDiscardAssignment_DoesNotRegister()
    {
        // `_ = db.SaveChangesAsync(token);` is not a direct SaveChanges invocation statement; a
        // synchronous rewrite would drop the token, so the fixer declines this wrapper shape.
        var test = WithExecuteUpdate(@"
class Program
{
    void Run(CancellationToken token)
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Name = ""Archived"";
        }|}
        _ = db.SaveChangesAsync(token);
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_TopLevelAsyncProgram_UsesExecuteUpdateAsync()
    {
        // Top-level programs have no async function ancestor, so async-ness is inferred from the
        // awaited trailing SaveChangesAsync rather than from an enclosing method modifier.
        const string topLevelUsings = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TestApp;
";

        var test = topLevelUsings + @"
using var db = new AppDbContext();
{|LC032:foreach (var user in db.Users.Where(u => u.IsActive))
{
    user.Name = ""Archived"";
}|}
await db.SaveChangesAsync();
" + EFCoreMockWithExecuteUpdate + TestTypes;

        var fixedCode = topLevelUsings + @"
using var db = new AppDbContext();

" + WarningComment + @"
await db.Users.Where(u => u.IsActive).ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Name, user => ""Archived""));
await db.SaveChangesAsync();
" + EFCoreMockWithExecuteUpdate + TestTypes;

        var verifier = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        verifier.TestState.OutputKind = OutputKind.ConsoleApplication;
        verifier.FixedState.OutputKind = OutputKind.ConsoleApplication;

        await verifier.RunAsync();
    }

    [Fact]
    public async Task Fixer_PreMaterializedLocal_DoesNotRegister()
    {
        // The collection is a local List<User> (await ...ToListAsync()); rewriting it to
        // users.ExecuteUpdate(...) is type-invalid and would orphan the local. Decline.
        var test = WithExecuteUpdate(@"
class Program
{
    async Task Run()
    {
        using var db = new AppDbContext();
        var users = await db.Users.Where(u => u.IsActive).ToListAsync();
        {|LC032:foreach (var user in users)
        {
            user.Name = ""Archived"";
        }|}
        await db.SaveChangesAsync();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_QueryLocal_DoesNotRegister()
    {
        // The collection is a local IQueryable; inlining its initializer would orphan the
        // local. v1 declines the local-source shape.
        var test = WithExecuteUpdate(@"
class Program
{
    void Run()
    {
        using var db = new AppDbContext();
        var users = db.Users.Where(u => u.IsActive);
        {|LC032:foreach (var user in users)
        {
            user.Name = ""Archived"";
        }|}
        db.SaveChanges();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_AsyncContext_NoExecuteUpdateAsync_DoesNotRegister()
    {
        // Diagnostic still fires (synchronous ExecuteUpdate exists), but the only async-context
        // rewrite would be a blocking sync-over-async call, so the fixer declines.
        var test = WithSyncOnly(@"
class Program
{
    async Task Run()
    {
        using var db = new AppDbContext();
        {|LC032:foreach (var user in db.Users.Where(u => u.IsActive))
        {
            user.Name = ""Archived"";
        }|}
        await db.SaveChangesAsync();
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_SyncLocalFunctionInsideAsyncMethod_UsesSyncExecuteUpdate()
    {
        // The nearest enclosing function is a synchronous local function, so await is illegal
        // here even though an async method is an ancestor: emit the synchronous form.
        var test = WithExecuteUpdate(@"
class Program
{
    async Task Run()
    {
        using var db = new AppDbContext();
        void Archive()
        {
            {|LC032:foreach (var user in db.Users.Where(u => u.IsActive))
            {
                user.Name = ""Archived"";
            }|}
            db.SaveChanges();
        }

        Archive();
        await Task.CompletedTask;
    }
}");

        var fixedCode = WithExecuteUpdate(@"
class Program
{
    async Task Run()
    {
        using var db = new AppDbContext();
        void Archive()
        {
            " + WarningComment + @"
            db.Users.Where(u => u.IsActive).ExecuteUpdate(setters => setters.SetProperty(user => user.Name, user => ""Archived""));
            db.SaveChanges();
        }

        Archive();
        await Task.CompletedTask;
    }
}");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }
}
