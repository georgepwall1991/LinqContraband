using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC016_AvoidDateTimeNow.AvoidDateTimeNowAnalyzer,
    LinqContraband.Analyzers.LC016_AvoidDateTimeNow.AvoidDateTimeNowFixer>;

namespace LinqContraband.Tests.Analyzers.LC016_AvoidDateTimeNow;

public class AvoidDateTimeNowFixerTests
{
    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
";

    [Fact]
    public async Task Fixer_ShouldExtractDateTimeNowToLocal()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<DateTime>().AsQueryable();
            var result = query.Where(x => x < {|LC016:DateTime.Now|}).ToList();
        }
    }
}";

        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<DateTime>().AsQueryable();
            var now = DateTime.Now;
            var result = query.Where(x => x < now).ToList();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldExtractDateTimeUtcNowToLocal()
    {
        var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<DateTime>().AsQueryable();
            var result = query.Where(x => x < {|LC016:DateTime.UtcNow|}).ToList();
        }
    }
}";

        var fixedCode = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<DateTime>().AsQueryable();
            var now = DateTime.UtcNow;
            var result = query.Where(x => x < now).ToList();
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }
}
