using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest =
    Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
        LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationAnalyzer,
        LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationFixer,
        Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC002_PrematureMaterialization;

public class PrematureMaterializationFixerTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Collections.Generic;
using TestNamespace;
";

    private const string MockNamespace = @"
namespace TestNamespace
{
    public class User
    {
        public int Age { get; set; }
    }

    public class DbContext
    {
        public IQueryable<User> Users => new List<User>().AsQueryable();
    }
}";

    [Fact]
    public async Task FixCrime_MovesWhereBeforeToList()
    {
        var test = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.ToList().Where(x => x.Age > 18);
    }
}
" + MockNamespace;

        var fixedCode = Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = db.Users.Where(x => x.Age > 18).ToList();
    }
}
" + MockNamespace;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            // Number of iterations to apply fix (default 1 check usually fine)
            NumberOfIncrementalIterations = 1,
            NumberOfFixAllIterations = 1
        };

        testObj.ExpectedDiagnostics.Add(new DiagnosticResult("LC002", DiagnosticSeverity.Warning)
            .WithSpan(12, 21, 12, 61)
            .WithArguments("Where"));

        await testObj.RunAsync();
    }
}