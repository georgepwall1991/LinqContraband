using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC039_NestedSaveChanges.NestedSaveChangesAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC039_NestedSaveChanges;

public partial class NestedSaveChangesTests
{
    [Fact]
    public async Task SaveInBranchThatReturns_ThenSaveAfterIf_DoesNotTrigger()
    {
        // Moonglade AddRequestCountCommand: insert-and-return on the first request, update otherwise.
        var test = EFCoreMock + Types + @"

class Program
{
    async Task<int> Run(TestApp.User user, bool isNew)
    {
        var db = new TestApp.AppDbContext();
        if (isNew)
        {
            await db.SaveChangesAsync();
            return 1;
        }

        user.Name = ""updated"";
        await db.SaveChangesAsync();
        return 2;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_ThenSaveAfterIf_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool failed)
    {
        var db = new TestApp.AppDbContext();
        if (failed)
        {
            db.SaveChanges();
            throw new InvalidOperationException();
        }

        db.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveBeforeIf_StillPairsWithSaveAfterEarlyExitBranch()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool isNew)
    {
        var db = new TestApp.AppDbContext();
        db.SaveChanges();
        if (isNew)
        {
            {|LC039:db.SaveChanges()|};
            return;
        }

        {|LC039:db.SaveChanges()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatDoesNotLeave_StillTriggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool isNew)
    {
        var db = new TestApp.AppDbContext();
        if (isNew)
        {
            db.SaveChanges();
        }

        {|LC039:db.SaveChanges()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInReturningBranch_WithSaveInFinally_StillTriggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool isNew)
    {
        var db = new TestApp.AppDbContext();
        try
        {
            if (isNew)
            {
                db.SaveChanges();
                return;
            }
        }
        finally
        {
            {|LC039:db.SaveChanges()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_CaughtBeforeLaterSave_StillTriggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag)
    {
        var db = new TestApp.AppDbContext();
        try
        {
            if (flag)
            {
                db.SaveChanges();
                throw new InvalidOperationException();
            }
        }
        catch
        {
        }

        {|LC039:db.SaveChanges()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_LaterSaveInSameTryBlock_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag)
    {
        var db = new TestApp.AppDbContext();
        try
        {
            if (flag)
            {
                db.SaveChanges();
                throw new InvalidOperationException();
            }

            db.SaveChanges();
        }
        catch
        {
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_InsideTryWithOnlyFinally_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag)
    {
        var db = new TestApp.AppDbContext();
        try
        {
            if (flag)
            {
                db.SaveChanges();
                throw new InvalidOperationException();
            }
        }
        finally
        {
            db.Dispose();
        }

        db.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInLambdaBranchThatThrows_OuterCatchDoesNotApply_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag)
    {
        try
        {
            Action run = () =>
            {
                var db = new TestApp.AppDbContext();
                if (flag)
                {
                    db.SaveChanges();
                    throw new InvalidOperationException();
                }

                db.SaveChanges();
            };
            run();
        }
        catch
        {
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInLambdaBranchThatReturns_ThenSaveInLambda_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag)
    {
        var db = new TestApp.AppDbContext();
        Action run = () =>
        {
            if (flag)
            {
                db.SaveChanges();
                return;
            }

            db.SaveChanges();
        };
        run();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
