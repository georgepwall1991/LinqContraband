using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationAnalyzer,
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationFixer>;

namespace LinqContraband.Tests.Analyzers.LC018_AvoidFromSqlRawWithInterpolation;

public partial class AvoidFromSqlRawWithInterpolationTests
{
    [Fact]
    public async Task FromSqlRaw_OnDbSet_WithUnsafeInterpolation_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using LinqContraband.Test.Data;" + EFCoreMock + EFCoreDbContextMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(AppDbContext db, int id)
        {
            var result = db.Users.FromSqlRaw({|LC018:$""SELECT * FROM Users WHERE Id = {id}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_OnDbContextSetT_WithUnsafeInterpolation_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using LinqContraband.Test.Data;" + EFCoreMock + EFCoreDbContextMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(AppDbContext db, int id)
        {
            var result = db.Set<User>().FromSqlRaw({|LC018:$""SELECT * FROM Users WHERE Id = {id}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_AsStaticExtensionCall_WithUnsafeInterpolation_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using LinqContraband.Test.Data;" + EFCoreMock + EFCoreDbContextMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(AppDbContext db, int id)
        {
            var result = RelationalQueryableExtensions.FromSqlRaw(db.Users, {|LC018:$""SELECT * FROM Users WHERE Id = {id}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlInterpolated_OnDbSet_ShouldNotTrigger()
    {
        // Negative guardrail: the safe sibling API must stay quiet on DbSet
        // receivers too, not just IQueryable wrappers, so the rule's
        // "provider variant" coverage isn't lopsided.
        var test = @"using Microsoft.EntityFrameworkCore;
using LinqContraband.Test.Data;" + EFCoreMock + EFCoreDbContextMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(AppDbContext db, int id)
        {
            var result = db.Users.FromSqlInterpolated($""SELECT * FROM Users WHERE Id = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlInterpolated_OnDbContextSetT_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using LinqContraband.Test.Data;" + EFCoreMock + EFCoreDbContextMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(AppDbContext db, int id)
        {
            var result = db.Set<User>().FromSqlInterpolated($""SELECT * FROM Users WHERE Id = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlInterpolated_AsStaticExtensionCall_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using LinqContraband.Test.Data;" + EFCoreMock + EFCoreDbContextMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(AppDbContext db, int id)
        {
            var result = RelationalQueryableExtensions.FromSqlInterpolated(db.Users, $""SELECT * FROM Users WHERE Id = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SqlQueryRaw_WithInterpolatedString_ShouldTriggerLC018()
    {
        // SqlQueryRaw<T> on the DatabaseFacade takes a raw string; an interpolated string with a
        // runtime hole is an injection sink, exactly like FromSqlRaw.
        var test = @"using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;" + EFCoreFacadeMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void Run(DatabaseFacade db, int id)
        {
            var rows = db.SqlQueryRaw<int>({|LC018:$""SELECT Id FROM Users WHERE Id = {id}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SqlQueryRaw_WithConcatenation_ShouldTriggerLC018()
    {
        var test = @"using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;" + EFCoreFacadeMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void Run(DatabaseFacade db, string name)
        {
            var rows = db.SqlQueryRaw<int>({|LC018:""SELECT Id FROM Users WHERE Name = '"" + name + ""'""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SqlQuery_WithFormattableString_ShouldNotTrigger()
    {
        // SqlQuery<T> takes a FormattableString and parameterizes the holes, so it is the safe
        // sibling and must stay quiet.
        var test = @"using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;" + EFCoreFacadeMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void Run(DatabaseFacade db, int id)
        {
            var rows = db.SqlQuery<int>($""SELECT Id FROM Users WHERE Id = {id}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SqlQueryRaw_WithConstantInterpolation_ShouldNotTrigger()
    {
        // Constant-only interpolation has no runtime hole, so it is not an injection vector.
        var test = @"using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;" + EFCoreFacadeMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        private const int Max = 10;
        public void Run(DatabaseFacade db)
        {
            var rows = db.SqlQueryRaw<int>($""SELECT TOP {Max} Id FROM Users"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Fixer_ShouldReplaceSqlQueryRawWithSqlQuery()
    {
        var test = @"using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;" + EFCoreFacadeMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void Run(DatabaseFacade db, int id)
        {
            var rows = db.SqlQueryRaw<int>({|LC018:$""SELECT Id FROM Users WHERE Id = {id}""|});
        }
    }
}";

        var fixedCode = @"using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;" + EFCoreFacadeMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void Run(DatabaseFacade db, int id)
        {
            var rows = db.SqlQuery<int>($""SELECT Id FROM Users WHERE Id = {id}"");
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldNotRegisterForSqlQueryRaw_WhenInterpolationIsInsideSqlStringLiteral()
    {
        var test = @"using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;" + EFCoreFacadeMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void Run(DatabaseFacade db, string name)
        {
            var rows = db.SqlQueryRaw<int>({|LC018:$""SELECT Id FROM Users WHERE Name = '{name}'""|});
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }
}
