using LinqContraband.Analyzers.LC002_PrematureMaterialization;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC002_PrematureMaterialization;

public class PrematureMaterializationTests
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
                public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) =>
                    Task.FromResult(source.ToList());
            }
        }
        """;

    [Fact]
    public async Task Reports_WhenWhereRunsAfterToList()
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

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("Where");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Reports_WhenCountRunsAfterToArray()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var count = {|#0:db.Users.ToArray().Count()|};
                }
            }
            """ + MockTypes;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("Count");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Reports_WhenOrderByRunsAfterAsEnumerable()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var ordered = {|#0:db.Users.AsEnumerable().OrderBy(x => x.Name)|};
                }
            }
            """ + MockTypes;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("OrderBy");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Reports_WhenToDictionaryFeedsWhere()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var filtered = {|#0:db.Users.ToDictionary(x => x.Id).Where(x => x.Value.Age > 18)|};
                }
            }
            """ + MockTypes;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("Where");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Reports_WhenToImmutableListFeedsWhere()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var filtered = {|#0:db.Users.ToImmutableList().Where(x => x.Age > 18)|};
                }
            }
            """ + MockTypes;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("Where");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Reports_WhenAwaitedToListAsyncFeedsWhere()
    {
        var test = CommonUsings + """

            class Program
            {
                async Task Main()
                {
                    var db = new DbContext();
                    var filtered = {|#0:(await db.Users.ToListAsync()).Where(x => x.Age > 18)|};
                }
            }
            """ + MockTypes;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("Where");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Reports_Redundant_AsEnumerableThenToList()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var users = {|#0:db.Users.AsEnumerable().ToList()|};
                }
            }
            """ + MockTypes;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.RedundantRule)
            .WithLocation(0)
            .WithArguments("ToList", "AsEnumerable");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Reports_Redundant_LocalMaterializedThenToList()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var materialized = db.Users.ToList();
                    var users = {|#0:materialized.ToList()|};
                }
            }
            """ + MockTypes;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.RedundantRule)
            .WithLocation(0)
            .WithArguments("ToList", "ToList");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DoesNotReport_WhenWhereRunsBeforeToList()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var query = db.Users.Where(x => x.Age > 18).ToList();
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_ForPureInMemoryEnumerable()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var users = new List<User>();
                    var filtered = users.ToList().Where(x => x.Age > 18);
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
