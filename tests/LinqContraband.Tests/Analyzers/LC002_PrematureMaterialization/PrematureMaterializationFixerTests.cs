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
    public async Task Fixes_InlineAnyPredicateByMovingItBeforeToList()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main(string name)
                {
                    var db = new DbContext();
                    var exists = {|#0:db.Users.ToList().Any(x => x.Name == name)|};
                }
            }
            """ + MockTypes;

        var fixedCode = CommonUsings + """

            class Program
            {
                void Main(string name)
                {
                    var db = new DbContext();
                    var exists = db.Users.Any(x => x.Name == name);
                }
            }
            """ + MockTypes;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("Any");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Fixes_InlineCountByRemovingEarlyToList()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var count = {|#0:db.Users.ToList().Count()|};
                }
            }
            """ + MockTypes;

        var fixedCode = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var count = db.Users.Count();
                }
            }
            """ + MockTypes;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("Count");

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
    public async Task DoesNotReport_RedundantToListThenAsEnumerable()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = db.Users.ToList().AsEnumerable();
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
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
                    var query = materialized.Where(x => x.Age > 18);
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotOfferFix_ForAsyncMaterialization()
    {
        var test = CommonUsings + """
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;

            class Program
            {
                async Task Main()
                {
                    var db = new DbContext();
                    var query = {|#0:(await db.Users.ToListAsync()).Where(x => x.Age > 18)|};
                }
            }

            namespace Microsoft.EntityFrameworkCore
            {
                public static class AsyncExtensions
                {
                    public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) =>
                        Task.FromResult(source.ToList());
                }
            }
            """ + MockTypes;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("Where");

        await VerifyCS.VerifyCodeFixAsync(test, expected, test);
    }

    [Fact]
    public async Task DoesNotOfferFix_ForConstructorMaterialization()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var materialized = new HashSet<User>(db.Users);
                    var query = materialized.Where(x => x.Age > 18);
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReportOrOfferFix_ForClientOnlyLambda()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = db.Users.ToList().Where(x => IsActive(x));
                }

                private static bool IsActive(User user) => user.Age > 18;
            }
            """ + MockTypes;

        await VerifyCS.VerifyCodeFixAsync(test, test);
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

    [Fact]
    public async Task FixAll_RewritesOnlyFixableDiagnostics()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var adults = {|#0:db.Users.ToList().Where(x => x.Age >= 18)|};
                    var materialized = db.Users.ToList();
                    var older = materialized.Where(x => x.Age >= 21);
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
                    var materialized = db.Users.ToList();
                    var older = materialized.Where(x => x.Age >= 21);
                }
            }
            """ + MockTypes;

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 1,
            CodeFixEquivalenceKey = "MoveBeforeMaterialization"
        };

        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult(PrematureMaterializationAnalyzer.Rule)
                .WithLocation(0)
                .WithArguments("Where"));

        await testObj.RunAsync();
    }
}
