using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC037_RawSqlStringConstruction.RawSqlStringConstructionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC037_RawSqlStringConstruction;

public partial class RawSqlStringConstructionTests
{
    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilder_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var sql = new StringBuilder()
                .Append(""UPDATE Users SET Name = '"")
                .Append(id)
                .Append(""'"")
                .ToString();

            var result = db.Database.ExecuteSqlRaw({|LC037:sql|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderStatementAppends_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            builder.Append(""'"");

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithConstantStringBuilderStatementAppends_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users "");
            builder.Append(""SET Active = 1"");

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderBranchSelectedLiteralAppendValue_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
		using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, bool active)
        {
            var builder = new StringBuilder();
            string predicate;
            if (active)
            {
                predicate = ""Active = 1"";
            }
            else
            {
                predicate = ""Active = 0"";
            }

            builder.Append(""UPDATE Users SET "");
            builder.Append(predicate);
            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendLocalNonConstantOnlyInReturningBranch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool skip)
        {
            var builder = new StringBuilder();
            var predicate = ""Active = 1"";
            if (skip)
            {
                predicate = id.ToString();
                return;
            }

            builder.Append(""UPDATE Users SET "");
            builder.Append(predicate);
            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderStatementAppendsClearedBeforeCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
		using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            builder.Clear();
            builder.Append(""UPDATE Users SET Active = 1"");

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderChainedClearBeforeCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"")
                .Append(id)
                .Clear();

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAliasAndLaterAppend_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var alias = builder;
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            builder.Append(""'"");

            var result = db.Database.ExecuteSqlRaw({|LC037:alias.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendThroughAlias_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var alias = builder;
            alias.Append(""UPDATE Users SET Name = '"");
            alias.Append(id);
            alias.Append(""'"");

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderClearThroughAliasBeforeCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var alias = builder;
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            alias.Clear();

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderClearThroughOriginalBeforeAliasCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var alias = builder;
            alias.Append(""UPDATE Users SET Name = '"");
            alias.Append(id);
            builder.Clear();

            var result = db.Database.ExecuteSqlRaw(alias.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderOriginalReassignedBeforeClear_ShouldStillTriggerAlias()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var alias = builder;
            alias.Append(""UPDATE Users SET Name = '"");
            alias.Append(id);
            builder = new StringBuilder();
            builder.Clear();

            var result = db.Database.ExecuteSqlRaw({|LC037:alias.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderOriginalReassignedBeforeAppend_ShouldNotTaintAlias()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var alias = builder;
            alias.Append(""UPDATE Users SET Active = 1"");
            builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);

            var result = db.Database.ExecuteSqlRaw(alias.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderSelfAssignmentAfterAppend_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            builder = builder;

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderConditionalSelfPreservingAssignment_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool keep)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            builder = keep ? builder : new StringBuilder();

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderConditionalAliasAppendIntoTarget_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool useBuilder)
        {
            var other = new StringBuilder();
            var builder = new StringBuilder();
            var alias = other;
            if (useBuilder)
            {
                alias = builder;
            }

            alias.Append(""UPDATE Users SET Name = '"");
            alias.Append(id);

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderConditionalAliasClear_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool useBuilder)
        {
            var other = new StringBuilder();
            var builder = new StringBuilder();
            var alias = other;
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            if (useBuilder)
            {
                alias = builder;
            }

            alias.Clear();

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAliasConditionallyReassignedBeforeClear_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool useOther)
        {
            var builder = new StringBuilder();
            var other = new StringBuilder();
            var alias = builder;
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            if (useOther)
            {
                alias = other;
            }

            alias.Clear();

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderConditionalAssignmentFromTaintedBuilder_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool useFresh)
        {
            var other = new StringBuilder();
            other.Append(""UPDATE Users SET Name = '"");
            other.Append(id);
            var builder = new StringBuilder();
            builder = useFresh ? new StringBuilder() : other;

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderCopyAssignmentFromTaintedBuilder_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            builder = new StringBuilder(builder.ToString());

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderCopyAssignmentFromTaintedBuilderExpression_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            builder = new StringBuilder(builder.ToString() + """");

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderCopyAssignmentFromOtherTaintedBuilder_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var other = new StringBuilder();
            other.Append(""UPDATE Users SET Name = '"");
            other.Append(id);

            var builder = new StringBuilder();
            builder = new StringBuilder(other.ToString());

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderVariableCapacityAndConstantAppends_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int capacity)
        {
            var builder = new StringBuilder(capacity);
            builder.Append(""UPDATE Users SET Active = 1"");

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderLoopCarriedAppendLocal_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var value = ""Active = 1"";

            for (var i = 0; i < 2; i++)
            {
                builder.Append(""UPDATE Users SET "");
                builder.Append(value);
                value = id.ToString();
            }

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderLoopValueResetBeforeAppend_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var value = id.ToString();

            for (var i = 0; i < 2; i++)
            {
                value = ""Active = 1"";
                builder.Append(""UPDATE Users SET "");
                builder.Append(value);
                value = id.ToString();
            }

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderCompoundAssignedAppendLocal_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var value = ""Active = "";
            value += id.ToString();
            builder.Append(""UPDATE Users SET "");
            builder.Append(value);

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderLoopCarriedCompoundAppendLocal_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var value = ""Active = 1"";

            for (var i = 0; i < 2; i++)
            {
                builder.Append(""UPDATE Users SET "");
                builder.Append(value);
                value += id.ToString();
            }

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderConstantCompoundAssignedAppendLocal_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var builder = new StringBuilder();
            var value = ""Active "";
            value += ""= 1"";
            builder.Append(""UPDATE Users SET "");
            builder.Append(value);

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithNullConditionalStringBuilderAppend_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            StringBuilder builder = new StringBuilder();
            builder?.Append(""UPDATE Users SET Name = '"");
            builder?.Append(id);

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderShortCircuitClear_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
		using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool shouldClear)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            var ignored = shouldClear && builder.Clear() != null;

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderShortCircuitAssignmentReset_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool shouldReset)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            var ignored = shouldReset && (builder = new StringBuilder()) != null;

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderFinallyClearBeforeCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            try
            {
                MaybeThrow();
            }
            finally
            {
                builder.Clear();
            }

            builder.Append(""UPDATE Users SET Active = 1"");
            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }

        private static void MaybeThrow() { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderConditionalFinallyClear_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool safe)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            try
            {
                MaybeThrow();
            }
            finally
            {
                if (safe)
                {
                    builder.Clear();
                }
            }

            builder.Append(""UPDATE Users SET Active = 1"");
            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }

        private static void MaybeThrow() { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderLoopGuardedClear_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
			using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool shouldClear, bool safe)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            while (shouldClear)
            {
                if (safe)
                {
                    builder.Clear();
                }
                else
                {
                    return;
                }
            }

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderClearInOnlyContinuingLoopBranch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool safe)
        {
            while (true)
            {
                var builder = new StringBuilder();
                builder.Append(""UPDATE Users SET Name = '"");
                builder.Append(id);
                if (safe)
                {
                    builder.Clear();
                }
                else
                {
                    continue;
                }

                builder.Append(""UPDATE Users SET Active = 1"");
                var result = db.Database.ExecuteSqlRaw(builder.ToString());
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderLocalAppendValue_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
		using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            var value = id;
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(value);

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderMethodCallAppendValue_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(GetName());

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }

        private static string GetName() => ""Alice"";
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderClearThenFluentStatementAppends_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            builder.Clear()
                .Append(""UPDATE Users SET Name = '"")
                .Append(id)
                .Append(""'"");

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderInitializerAppendsClearedBeforeCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder()
                .Append(""UPDATE Users SET Name = '"")
                .Append(id);

            builder.Clear();
            builder.Append(""UPDATE Users SET Active = 1"");

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

}
