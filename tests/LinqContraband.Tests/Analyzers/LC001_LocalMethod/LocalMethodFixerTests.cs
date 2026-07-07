using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer,
    LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer,
    LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodFixer>;

namespace LinqContraband.Tests.Analyzers.LC001_LocalMethod;

public partial class LocalMethodFixerTests
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

    public class Role
    {
        public int UserAge { get; set; }
    }

    public class DbContext
    {
        public IQueryable<User> Users => new List<User>().AsQueryable();
        public IQueryable<Role> Roles => new List<Role>().AsQueryable();
    }
}";

    [Fact]
    public async Task FixCrime_SwitchToClientSideEvaluation()
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

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.AsEnumerable().Where(u => CalculateAge(u.Dob) > 18);
    }

    int CalculateAge(DateTime dob) => 0;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            CompilerDiagnostics = CompilerDiagnostics.Errors // Allow errors if any, though this should be valid
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithSpan(13, 41, 13, 60)
            .WithArguments("CalculateAge"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_NestedCorrelatedLocalMethod_HasNoFix()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users
            .Where(u => db.Users
                .Any(inner => {|#0:IsMatch(inner.Age, u.Age)|}));
    }

    bool IsMatch(int a, int b) => a == b;
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users
            .Where(u => db.Users
                .Any(inner => IsMatch(inner.Age, u.Age)));
    }

    bool IsMatch(int a, int b) => a == b;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("IsMatch"));
        testObj.FixedState.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithSpan(15, 31, 15, 56)
            .WithArguments("IsMatch"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_QueryBuiltInsideLambda_StillOffersFix()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        Func<IEnumerable<User>> build = () => db.Users.Where(u => {|#0:IsAdult(u.Age)|});
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        Func<IEnumerable<User>> build = () => db.Users.AsEnumerable().Where(u => IsAdult(u.Age));
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("IsAdult"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixCrime_EnumerableLambdaInsideQuery_FixesOuterQuery()
    {
        var test = Usings + @"
class Program
{
    void Main(List<string> names)
    {
        var db = new DbContext();
        var query = db.Users.Where(u => names.Any(name => {|#0:IsAllowed(u.Age, name)|}));
    }

    bool IsAllowed(int age, string name) => age > 0 && name.Length > 0;
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main(List<string> names)
    {
        var db = new DbContext();
        var query = db.Users.AsEnumerable().Where(u => names.Any(name => IsAllowed(u.Age, name)));
    }

    bool IsAllowed(int age, string name) => age > 0 && name.Length > 0;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("IsAllowed"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task FixAll_SwitchesAllLocalMethodCallsToClientSide()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var adults = db.Users.Where(u => {|#0:CalculateAge(u.Dob)|} > 18);
        var sorted = db.Users.Where(u => {|#1:IsActive(u)|} && u.Age > 20);
    }

    int CalculateAge(DateTime dob) => 0;
    bool IsActive(User u) => true;
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var adults = db.Users.AsEnumerable().Where(u => CalculateAge(u.Dob) > 18);
        var sorted = db.Users.AsEnumerable().Where(u => IsActive(u) && u.Age > 20);
    }

    int CalculateAge(DateTime dob) => 0;
    bool IsActive(User u) => true;
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "SwitchToClientSide"
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("CalculateAge"));
        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC001", DiagnosticSeverity.Warning)
            .WithLocation(1)
            .WithArguments("IsActive"));

        await testObj.RunAsync();
    }

}
