using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation.AvoidFromSqlRawWithInterpolationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC018_AvoidFromSqlRawWithInterpolation;

public partial class AvoidFromSqlRawWithInterpolationTests
{
    [Fact]
    public async Task FromSqlRaw_WithNoHoleInterpolatedString_ShouldNotTriggerLC018()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw($""SELECT * FROM Table"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithConstantOnlyInterpolation_ShouldNotTriggerLC018()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        private const int Active = 1;

        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw($""SELECT * FROM Table WHERE Active = {Active}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithConstantLocalInterpolation_ShouldNotTriggerLC018()
    {
        // const locals are compile-time constants too, not just const fields;
        // both flows produce IOperation.ConstantValue.HasValue == true.
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            const int Active = 1;
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw($""SELECT * FROM Table WHERE Active = {Active}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithNumericLiteralInterpolation_ShouldNotTriggerLC018()
    {
        // A bare numeric literal in an interpolation hole is always a
        // compile-time constant and cannot embed runtime data.
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw($""SELECT * FROM Table WHERE Id = {1}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithMultipleConstantHoles_ShouldNotTriggerLC018()
    {
        // Two holes, both compile-time constants: HasNonConstantInterpolation
        // must require *every* hole to be constant for the safe-shape gate to
        // hold.
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        private const int Active = 1;
        private const int Limit = 100;

        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw($""SELECT TOP {Limit} * FROM Table WHERE Active = {Active}"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithNameofInterpolation_ShouldNotTriggerLC018()
    {
        // `nameof(...)` is a compile-time constant string; embedding it into
        // a raw SQL fragment is identifier-only and cannot smuggle user data.
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw($""SELECT {nameof(System.Int32.MaxValue)} FROM Table"");
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithMixedConstantAndRuntimeHoles_ShouldTriggerLC018()
    {
        // Boundary lock-in: a single non-constant hole alongside a constant
        // hole must still trigger LC018. The constant hole does not launder
        // the runtime-data hole next to it.
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        private const int Active = 1;

        public void TestMethod(int id)
        {
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw({|LC018:$""SELECT * FROM Table WHERE Active = {Active} AND Id = {id}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_WithStaticReadonlyFieldInterpolation_ShouldTriggerLC018()
    {
        // Boundary lock-in: `static readonly` is not a compile-time constant -
        // the field's runtime value is observable and can be reassigned via
        // reflection or static initializers. LC018 must treat it as
        // potentially unsafe even though the typical value is fixed.
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        private static readonly int Active = 1;

        public void TestMethod()
        {
            var query = new int[0].AsQueryable();
            var result = query.FromSqlRaw({|LC018:$""SELECT * FROM Table WHERE Active = {Active}""|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
