using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC001_LocalMethod;

public class LocalMethodSmugglerEdgeCasesTests
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
        public string Name { get; set; }
    }

    public class DbContext
    {
        public IQueryable<User> Users => new List<User>().AsQueryable();
    }
}";

    [Fact]
    public async Task TestCrime_NestedLambda_LocalMethodInInnerLambda_ShouldTriggerLC001()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users
            .Where(u => db.Users
                .Any(inner => IsMatch(inner.Name, u.Name)));
    }

    bool IsMatch(string a, string b) => a == b;
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC001")
            .WithSpan(15, 31, 15, 58)
            .WithArguments("IsMatch");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_NestedLambda_LocalMethodInOuterLambda_ShouldTriggerLC001()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users
            .Where(u => CalculateAge(u.Dob) > 18 && u.Age > 0);
    }

    int CalculateAge(DateTime dob) => 0;
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC001")
            .WithSpan(14, 25, 14, 44)
            .WithArguments("CalculateAge");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_ExpressionBodiedMember_CallingLocalMethod_ShouldTriggerLC001()
    {
        var test = Usings + @"
class Program
{
    DbContext db = new DbContext();

    IQueryable<User> Adults => db.Users.Where(u => IsAdult(u.Age));

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC001")
            .WithSpan(12, 52, 12, 66)
            .WithArguments("IsAdult");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_MethodGroupOnList_ShouldNotTrigger()
    {
        // Method groups on non-IQueryable (List) should not trigger
        var test = Usings + @"
class Program
{
    void Main()
    {
        var list = new List<User>();
        var results = list.Where(IsValid);
    }

    bool IsValid(User u) => u.Age > 18;
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_SelectWithPropertyAccess_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.Select(u => u.Name);
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_LinqMethodChainNoLocalMethod_ShouldNotTrigger()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users
            .Where(u => u.Age > 18)
            .Select(u => u.Name);
    }
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestCrime_LocalMethodInSelect_ShouldTriggerLC001()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.Select(u => FormatName(u.Name));
    }

    string FormatName(string name) => name.Trim();
}
" + MockNamespace;

        var expected = VerifyCS.Diagnostic("LC001")
            .WithSpan(13, 42, 13, 60)
            .WithArguments("FormatName");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_LambdaOnIEnumerable_ShouldNotTrigger()
    {
        // Calling a local method on IEnumerable (not IQueryable) should not trigger
        var test = Usings + @"
class Program
{
    void Main()
    {
        IEnumerable<User> users = new List<User>();
        var results = users.Where(u => IsAdult(u.Age));
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
