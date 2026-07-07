using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate.MissingWhereBeforeExecuteDeleteUpdateAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate;

public partial class MissingWhereBeforeExecuteDeleteUpdateTests
{
    [Fact]
    public async Task ExecuteDelete_UnconditionalFilterThenOptionalNarrowing_ShouldNotTrigger()
    {
        // The base query is filtered on every path; the if only adds a further Where, so the delete
        // can never affect the whole table.
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, bool flag)
        {
            var q = db.Set<User>().Where(u => u.Id > 10);
            if (flag)
            {
                q = q.Where(u => u.Id < 100);
            }
            return q.ExecuteDelete();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_ConditionalReassignToUnfiltered_ShouldTrigger()
    {
        // The if path reassigns q to an UNfiltered query, so on that path the delete affects the whole
        // table — must still report (the unconditional base being filtered is not enough).
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, bool flag)
        {
            var q = db.Set<User>().Where(u => u.Id > 10);
            if (flag)
            {
                q = db.Set<User>();
            }
            return {|LC035:q.ExecuteDelete()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_EarlierConditionalUnfilteredAssignmentOverwrittenByFilteredBase_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, bool flag)
        {
            IQueryable<User> query = db.Set<User>();
            if (flag)
            {
                query = db.Set<User>();
            }

            query = db.Set<User>().Where(u => u.Id > 10);

            return query.ExecuteDelete();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_IfElseFilteredAssignmentsWithoutUnconditionalBase_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, bool flag)
        {
            IQueryable<User> query;
            if (flag)
            {
                query = db.Set<User>().Where(u => u.Id > 10);
            }
            else
            {
                query = db.Set<User>().Where(u => u.Id < 100);
            }

            return query.ExecuteDelete();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_EarlierConditionalAssignmentOverwrittenByFilteredIfElse_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, bool debug, bool flag)
        {
            IQueryable<User> query;
            if (debug)
            {
                query = db.Set<User>();
            }

            if (flag)
            {
                query = db.Set<User>().Where(u => u.Id > 10);
            }
            else
            {
                query = db.Set<User>().Where(u => u.Id < 100);
            }

            return query.ExecuteDelete();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_EarlierUnfilteredIfElseOverwrittenByFilteredIfElse_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } public bool IsActive { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, bool debug, bool flag)
        {
            IQueryable<User> query;
            if (debug)
            {
                query = db.Set<User>();
            }
            else
            {
                query = db.Set<User>();
            }

            if (flag)
            {
                query = db.Set<User>().Where(u => u.Id > 10);
            }
            else
            {
                query = db.Set<User>().Where(u => u.Id < 100);
            }

            return query.ExecuteDelete();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_FilteredIfElseThenOptionalFilteredNarrowing_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } public bool IsActive { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, bool flag, bool activeOnly)
        {
            IQueryable<User> query;
            if (flag)
            {
                query = db.Set<User>().Where(u => u.Id > 10);
            }
            else
            {
                query = db.Set<User>().Where(u => u.Id < 100);
            }

            if (activeOnly)
            {
                query = query.Where(u => u.IsActive);
            }

            return query.ExecuteDelete();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_FilteredIfElseThenOptionalUnfilteredReassignment_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, bool flag, bool reset)
        {
            IQueryable<User> query;
            if (flag)
            {
                query = db.Set<User>().Where(u => u.Id > 10);
            }
            else
            {
                query = db.Set<User>().Where(u => u.Id < 100);
            }

            if (reset)
            {
                query = db.Set<User>();
            }

            return {|LC035:query.ExecuteDelete()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_AllFilteredConditionalReceiver_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, bool flag)
        {
            return (flag
                ? db.Set<User>().Where(u => u.Id > 10)
                : db.Set<User>().Where(u => u.Id < 100))
                .ExecuteDelete();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteUpdate_AllFilteredSwitchExpressionReceiver_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, int mode)
        {
            return (mode switch
            {
                1 => db.Set<User>().Where(u => u.Id > 10),
                _ => db.Set<User>().Where(u => u.Id < 100),
            }).ExecuteUpdate();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_AllFilteredConditionalReceiverReusingLocal_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, bool flag)
        {
            var filtered = db.Set<User>().Where(u => u.Id > 10);

            return (flag ? filtered : filtered).ExecuteDelete();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteUpdate_AllFilteredSwitchExpressionReceiverReusingLocal_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, int mode)
        {
            var filtered = db.Set<User>().Where(u => u.Id > 10);

            return (mode switch
            {
                1 => filtered,
                _ => filtered,
            }).ExecuteUpdate();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_ConditionalReceiverWithUnfilteredArm_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, bool flag)
        {
            return {|LC035:(flag
                ? db.Set<User>().Where(u => u.Id > 10)
                : db.Set<User>())
                .ExecuteDelete()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteUpdate_SwitchExpressionReceiverWithUnfilteredArm_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, int mode)
        {
            return {|LC035:(mode switch
            {
                1 => db.Set<User>().Where(u => u.Id > 10),
                _ => db.Set<User>(),
            }).ExecuteUpdate()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_ConditionalReassignToFilteredLocal_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, bool flag)
        {
            var baseQuery = db.Set<User>().Where(u => u.Id > 10);
            var archived = baseQuery.Where(u => u.Id < 100);
            IQueryable<User> query = baseQuery;

            if (flag)
            {
                query = archived;
            }

            return query.ExecuteDelete();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteDelete_OptionalReassignmentReusingFilteredBaseLocal_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, bool flag)
        {
            var filtered = db.Set<User>().Where(u => u.Id > 10);
            IQueryable<User> query = filtered;

            if (flag)
            {
                query = filtered;
            }

            return query.ExecuteDelete();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteUpdate_MultipleOptionalFilteredReassignments_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } public bool IsActive { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db, bool activeOnly, bool smallBatch)
        {
            var query = db.Set<User>().Where(u => u.Id > 10);

            if (activeOnly)
            {
                query = query.Where(u => u.IsActive);
            }

            if (smallBatch)
            {
                query = query.Where(u => u.Id < 100);
            }

            return query.ExecuteUpdate();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteUpdate_CatchPathReassignsToUnfilteredQuery_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class User { public int Id { get; set; } }

    public sealed class Program
    {
        public int Run(DbContext db)
        {
            var query = db.Set<User>().Where(u => u.Id > 10);

            try
            {
                query = query.Where(u => u.Id < 100);
            }
            catch (System.Exception)
            {
                query = db.Set<User>();
            }

            return {|LC035:query.ExecuteUpdate()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
