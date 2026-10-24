using LinqContraband.Analyzers.LC002_PrematureMaterialization;
using VerifyAnalyzer = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationAnalyzer,
    LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationFixer>;

namespace LinqContraband.Tests.Analyzers.LC002_PrematureMaterialization;

/// <summary>
/// Leftover LC002 immutable/set materializer names the shipped fixtures do not isolate.
/// <c>ToImmutableList().Where</c> and <c>ToImmutableHashSet().ToList</c> already exist;
/// <c>ToImmutableSortedSet</c> and <c>ToHashSetAsync</c> as de-duplicating sources,
/// the <c>ToImmutableArray</c> move-before-materialization fixer (via <c>OrderBy</c>),
/// and <c>ToImmutableArray</c>/<c>ToImmutableList</c> as both sides of a redundant pair
/// are leftover. Do not merge these names into the shared LC002 files.
/// <c>ToImmutableArray().Where</c> binds to <c>ImmutableArrayExtensions.Where</c>, not
/// <c>Enumerable.Where</c>, and is a pre-existing FN — do not lock it as quiet or as a positive.
/// </summary>
public class PrematureMaterializationCoverageTests
{
    private const string CommonUsings = """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Linq;
        using System.Threading.Tasks;
        using TestNamespace;
        using Microsoft.EntityFrameworkCore;
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

        namespace Microsoft.EntityFrameworkCore
        {
            public static class AsyncExtensions
            {
                public static Task<HashSet<T>> ToHashSetAsync<T>(this IQueryable<T> source) =>
                    Task.FromResult(source.ToHashSet());
            }
        }
        """;

    [Fact]
    public async Task ToImmutableArray_ThenOrderBy_Reports()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var ordered = {|#0:db.Users.ToImmutableArray().OrderBy(x => x.Name)|};
                }
            }
            """ + MockTypes;

        var expected = VerifyAnalyzer.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("OrderBy");

        await VerifyAnalyzer.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ToImmutableSortedSet_ThenToList_StaysQuiet()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var users = db.Users.Select(u => u.Id).ToImmutableSortedSet().ToList();
                }
            }
            """ + MockTypes;

        await VerifyAnalyzer.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ToHashSetAsync_ThenToList_StaysQuiet()
    {
        var test = CommonUsings + """

            class Program
            {
                async Task Main()
                {
                    var db = new DbContext();
                    var users = (await db.Users.ToHashSetAsync()).ToList();
                }
            }
            """ + MockTypes;

        await VerifyAnalyzer.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Fixes_InlineOrderByByMovingItBeforeToImmutableArray()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = {|#0:db.Users.ToImmutableArray().OrderBy(x => x.Name)|};
                }
            }
            """ + MockTypes;

        var fixedCode = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = db.Users.OrderBy(x => x.Name).ToImmutableArray();
                }
            }
            """ + MockTypes;

        var expected = VerifyFix.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("OrderBy");

        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Fixes_RedundantToListThenToImmutableArray()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = {|#0:db.Users.ToList().ToImmutableArray()|};
                }
            }
            """ + MockTypes;

        var fixedCode = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = db.Users.ToImmutableArray();
                }
            }
            """ + MockTypes;

        var expected = VerifyFix.Diagnostic(PrematureMaterializationAnalyzer.RedundantRule)
            .WithLocation(0)
            .WithArguments("ToImmutableArray", "ToList");

        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Fixes_RedundantToImmutableArrayThenToList()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = {|#0:db.Users.ToImmutableArray().ToList()|};
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

        var expected = VerifyFix.Diagnostic(PrematureMaterializationAnalyzer.RedundantRule)
            .WithLocation(0)
            .WithArguments("ToList", "ToImmutableArray");

        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Fixes_RedundantToListThenToImmutableList()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = {|#0:db.Users.ToList().ToImmutableList()|};
                }
            }
            """ + MockTypes;

        var fixedCode = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = db.Users.ToImmutableList();
                }
            }
            """ + MockTypes;

        var expected = VerifyFix.Diagnostic(PrematureMaterializationAnalyzer.RedundantRule)
            .WithLocation(0)
            .WithArguments("ToImmutableList", "ToList");

        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task Fixes_RedundantToImmutableListThenToList()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = {|#0:db.Users.ToImmutableList().ToList()|};
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

        var expected = VerifyFix.Diagnostic(PrematureMaterializationAnalyzer.RedundantRule)
            .WithLocation(0)
            .WithArguments("ToList", "ToImmutableList");

        await VerifyFix.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
