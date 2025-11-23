using VerifyCS =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC001_LocalMethod;

public class LocalMethodSmugglerTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections.Generic;
using TestNamespace;
";

    private const string MockNamespace = @"
namespace TestNamespace
{
    public class User
    {
        public int Age { get; set; }
        public DateTime Dob { get; set; }
    }

    public class DbContext
    {
        public IQueryable<User> Users => new List<User>().AsQueryable();
    }
}";

    [Fact]
    public async Task TestCrime_LocalMethodInWhere_ShouldTriggerLC001()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.Where(u => CalculateAge(u.Dob) > 18);
    }

    int CalculateAge(DateTime dob) => 0;
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC001")
            .WithSpan(13, 41, 13, 60)
            .WithArguments("CalculateAge");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_PropertyAccess_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.Where(u => u.Age > 18);
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestExempt_SystemMethod_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        // Math.Abs is defined in System (Metadata), not source, so it should be exempt.
        var query = db.Users.Where(u => Math.Abs(u.Age) > 18);
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}