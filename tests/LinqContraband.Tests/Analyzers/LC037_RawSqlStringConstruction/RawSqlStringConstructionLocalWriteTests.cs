using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC037_RawSqlStringConstruction.RawSqlStringConstructionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC037_RawSqlStringConstruction;

public partial class RawSqlStringConstructionTests
{
    [Fact]
    public async Task ExecuteSqlRaw_WithConstructedInitialValueOverwrittenByConstantBeforeCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var sql = ""UPDATE Users SET Name = "" + id;
            sql = ""UPDATE Users SET Active = 1"";
            var result = db.Database.ExecuteSqlRaw(sql);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithConstantInitialValueOverwrittenByConstructedSqlBeforeCall_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var sql = ""UPDATE Users SET Active = 1"";
            sql = ""UPDATE Users SET Name = "" + id;
            var result = db.Database.ExecuteSqlRaw({|LC037:sql|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithSelfReferentialConcatAssignment_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var sql = ""UPDATE Users SET Name = "";
            sql = sql + id;
            var result = db.Database.ExecuteSqlRaw({|LC037:sql|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithConstructedInitialValueConditionallyOverwrittenByConstant_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool useSafeSql)
        {
            var sql = ""UPDATE Users SET Name = "" + id;
            if (useSafeSql)
            {
                sql = ""UPDATE Users SET Active = 1"";
            }

            var result = db.Database.ExecuteSqlRaw({|LC037:sql|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithConstantInitialValueConditionallyOverwrittenByConstructedSql_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool useNameSql)
        {
            var sql = ""UPDATE Users SET Active = 1"";
            if (useNameSql)
            {
                sql = ""UPDATE Users SET Name = "" + id;
            }

            var result = db.Database.ExecuteSqlRaw({|LC037:sql|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithConditionalConstructedSqlOverwrittenByConstantBeforeCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool useNameSql)
        {
            var sql = ""UPDATE Users SET Active = 1"";
            if (useNameSql)
            {
                sql = ""UPDATE Users SET Name = "" + id;
            }

            sql = ""UPDATE Users SET Active = 1"";
            var result = db.Database.ExecuteSqlRaw(sql);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
