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

    [Fact]
    public async Task SaveAfterExclusiveEarlierSave_StillPairsWithReachableSaveBeforeIf()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool a, bool b)
    {
        var db = new TestApp.AppDbContext();
        db.SaveChanges();
        if (a)
        {
            {|LC039:db.SaveChanges()|};
        }
        else
        {
            if (b)
            {
                {|LC039:db.SaveChanges()|};
                return;
            }

            {|LC039:db.SaveChanges()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveAfterExclusiveEarlierSave_WithNoReachableSave_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool a, bool b)
    {
        var db = new TestApp.AppDbContext();
        if (a)
        {
            db.SaveChanges();
        }
        else
        {
            if (b)
            {
                db.SaveChanges();
                return;
            }

            db.SaveChanges();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_CatchReturns_DoesNotTrigger()
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
            return;
        }

        db.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_CatchRethrows_DoesNotTrigger()
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
        catch (InvalidOperationException)
        {
            throw;
        }

        db.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_FirstMatchingCatchRethrows_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag, bool retry)
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
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            Console.WriteLine();
        }

        db.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_FirstMatchingCatchResumes_StillTriggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag, bool retry)
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
        catch (InvalidOperationException)
        {
            Console.WriteLine();
        }
        catch
        {
            throw;
        }

        {|LC039:db.SaveChanges()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_UnrelatedTypedCatch_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag, bool retry)
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
        catch (ArgumentException)
        {
            Console.WriteLine();
        }

        db.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_FilteredCatchResumes_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag, bool retry)
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
        catch (InvalidOperationException) when (retry)
        {
            Console.WriteLine();
        }

        db.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_BaseTypeCatchResumes_StillTriggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag, bool retry)
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
        catch (SystemException)
        {
            Console.WriteLine();
        }

        {|LC039:db.SaveChanges()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_CatchReturnsOrRethrowsOnEveryPath_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag, bool retry)
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
            if (retry)
                return;
            else
                throw;
        }

        db.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_CatchSometimesReturns_StillTriggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag, bool retry)
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
            if (retry)
                return;
        }

        {|LC039:db.SaveChanges()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TransactionBoundaryOnlyInEarlyExitBranch_DoesNotSeparateSurroundingSaves()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag)
    {
        var db = new TestApp.AppDbContext();
        db.SaveChanges();
        if (flag)
        {
            using var tx = db.Database.BeginTransaction();
            db.SaveChanges();
            tx.Commit();
            return;
        }

        {|LC039:db.SaveChanges()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TransactionBoundaryOnSharedPath_StillSeparatesSaves()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag)
    {
        var db = new TestApp.AppDbContext();
        db.SaveChanges();
        using var tx = db.Database.BeginTransaction();
        if (flag)
        {
            db.SaveChanges();
            return;
        }

        db.SaveChanges();
        tx.Commit();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TransactionBoundaryInSwitchSectionReachedByGotoCase_SeparatesSaves()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(int mode)
    {
        var db = new TestApp.AppDbContext();
        db.SaveChanges();
        switch (mode)
        {
            case 1:
                db.Database.BeginTransaction();
                goto case 2;
            case 2:
                db.SaveChanges();
                break;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TransactionBoundaryInExclusiveSwitchSection_DoesNotSeparateSaves()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(int mode)
    {
        var db = new TestApp.AppDbContext();
        db.SaveChanges();
        switch (mode)
        {
            case 1:
                db.Database.BeginTransaction();
                break;
            case 2:
                {|LC039:db.SaveChanges()|};
                break;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_FilteredCatchRethrowsBeforeResumingCatch_DoesNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag, bool retry)
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
        catch (InvalidOperationException) when (retry)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine();
        }

        db.SaveChanges();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_FilteredAndUnfilteredCatchesResume_StillTriggers()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(bool flag, bool retry)
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
        catch (InvalidOperationException) when (retry)
        {
            Console.WriteLine();
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine();
        }

        {|LC039:db.SaveChanges()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SavesInSwitchSections_GotoLabelOutsideSwitch_DoNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(int mode)
    {
        var db = new TestApp.AppDbContext();
        switch (mode)
        {
            case 0:
                db.SaveChanges();
                goto Done;
            case 1:
                db.SaveChanges();
                break;
        }

        Done:
        Console.WriteLine();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SavesInSwitchSections_GotoLabelInSameSection_DoNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(int mode, bool again)
    {
        var db = new TestApp.AppDbContext();
        switch (mode)
        {
            case 0:
                Retry:
                db.SaveChanges();
                if (again)
                {
                    again = false;
                    goto Retry;
                }

                break;
            case 1:
                db.SaveChanges();
                break;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SavesInSwitchSections_GotoCaseIntoOtherSection_KnownFalseNegative_DoesNotTrigger()
    {
        // Known false negative: both saves run, but LC039 does not follow goto and keeps switch sections exclusive.
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(int mode)
    {
        var db = new TestApp.AppDbContext();
        switch (mode)
        {
            case 0:
                db.SaveChanges();
                goto case 1;
            case 1:
                db.SaveChanges();
                break;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SavesInSwitchSections_GotoLabelInOtherSectionsLocalFunction_DoNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(int mode)
    {
        var db = new TestApp.AppDbContext();
        switch (mode)
        {
            case 0:
                db.SaveChanges();
                goto Done;
            case 1:
                db.SaveChanges();
                void Local()
                {
                    // Shadows the outer label (CS0158), as code being edited in the IDE may; the goto in case 0
                    // still targets the label after the switch.
                    {|CS0158:Done|}:
                    Console.WriteLine();
                }

                Local();
                break;
        }

        Done:
        Console.WriteLine();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_ConstantFalseFilterRethrows_StillTriggers()
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
        catch (InvalidOperationException) when (false)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine();
        }

        {|LC039:db.SaveChanges()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_ConstantTrueFilterResumes_StillTriggers()
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
        catch (InvalidOperationException) when (true)
        {
            Console.WriteLine();
        }
        catch
        {
            throw;
        }

        {|LC039:db.SaveChanges()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SavesInSwitchSections_GotoBeforeSourceSave_DoNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(int mode, bool skip)
    {
        var db = new TestApp.AppDbContext();
        switch (mode)
        {
            case 0:
                if (skip)
                    goto case 1;
                db.SaveChanges();
                break;
            case 1:
                db.SaveChanges();
                break;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SaveInBranchThatThrows_CatchGoesToLabelPastLaterSave_DoesNotTrigger()
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
            goto Done;
        }

        db.SaveChanges();
        Done:
        Console.WriteLine();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SavesInSwitchSections_ReverseGotoCaseAfterBoundary_DoNotTrigger()
    {
        var test = EFCoreMock + Types + @"

class Program
{
    void Run(int mode)
    {
        var db = new TestApp.AppDbContext();
        switch (mode)
        {
            case 0:
                db.SaveChanges();
                break;
            case 1:
                db.SaveChanges();
                db.Database.BeginTransaction();
                goto case 0;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
