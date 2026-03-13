using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationAnalyzer,
    LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationAnalyzer,
    LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationFixer>;
using LinqContraband.Analyzers.LC002_PrematureMaterialization;

namespace LinqContraband.Tests.Analyzers.LC002_PrematureMaterialization;

public class PrematureMaterializationFixerTests
{
    private const string CommonUsings = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using TestNamespace;
        """;

    private const string MockTypes = """
        namespace TestNamespace
        {
            public class User
            {
                public int Id { get; set; }
                public int Age { get; set; }
                public string Name { get; set; } = "";
            }

            public class DbContext
            {
                public IQueryable<User> Users => new List<User>().AsQueryable();
            }
        }
        """;

    [Fact]
    public async Task Fixes_InlineWhereByMovingItBeforeToList()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = {|#0:db.Users.ToList().Where(x => x.Age > 18)|};
                }
            }
            """ + MockTypes;

        var fixedCode = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = db.Users.Where(x => x.Age > 18).ToList();
                }
            }
            """ + MockTypes;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("Where");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Fixes_InlineWhereWhilePreservingOuterMaterializer()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = {|#0:db.Users.ToList().Where(x => x.Age > 18)|}.ToArray();
                }
            }
            """ + MockTypes;

        var fixedCode = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = db.Users.Where(x => x.Age > 18).ToArray();
                }
            }
            """ + MockTypes;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("Where");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Fixes_RedundantToArrayThenToList()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = {|#0:db.Users.ToArray().ToList()|};
                }
            }
            """ + MockTypes;

        var fixedCode = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = db.Users.ToList();
                }
            }
            """ + MockTypes;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.RedundantRule)
            .WithLocation(0)
            .WithArguments("ToList", "ToArray");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Fixes_RedundantToListThenAsEnumerable()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = {|#0:db.Users.ToList().AsEnumerable()|};
                }
            }
            """ + MockTypes;

        var fixedCode = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = db.Users.ToList();
                }
            }
            """ + MockTypes;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.RedundantRule)
            .WithLocation(0)
            .WithArguments("AsEnumerable", "ToList");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task DoesNotOfferFix_ForShapeChangingToDictionaryCase()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = {|#0:db.Users.ToDictionary(x => x.Id).Where(x => x.Value.Age > 18)|};
                }
            }
            """ + MockTypes;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("Where");

        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
    }

    [Fact]
    public async Task DoesNotOfferFix_ForLocalMaterialization()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var materialized = db.Users.ToList();
                    var query = {|#0:materialized.Where(x => x.Age > 18)|};
                }
            }
            """ + MockTypes;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("Where");

        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
    }

    [Fact]
    public async Task FixAll_RewritesAllInlineMoveBeforeMaterializationCases()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var adults = {|#0:db.Users.ToList().Where(x => x.Age >= 18)|};
                    var ordered = {|#1:db.Users.ToArray().OrderBy(x => x.Name)|};
                }
            }
            """ + MockTypes;

        var fixedCode = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var adults = db.Users.Where(x => x.Age >= 18).ToList();
                    var ordered = db.Users.OrderBy(x => x.Name).ToArray();
                }
            }
            """ + MockTypes;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "MoveBeforeMaterialization"
        };

        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult(PrematureMaterializationAnalyzer.Rule)
                .WithLocation(0)
                .WithArguments("Where"));
        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult(PrematureMaterializationAnalyzer.Rule)
                .WithLocation(1)
                .WithArguments("OrderBy"));

        await testObj.RunAsync();
    }
}
