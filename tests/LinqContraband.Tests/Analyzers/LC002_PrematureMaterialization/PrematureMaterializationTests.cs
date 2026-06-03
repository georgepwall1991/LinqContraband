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
    public async Task Reports_WhenWhereWithCapturedValueRunsAfterToList()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main(int minAge)
                {
                    var db = new DbContext();
                    var query = {|#0:db.Users.ToList().Where(x => x.Age > minAge)|};
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
    public async Task Reports_WhenAnyPredicateRunsAfterToList()
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

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("Any");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task Reports_WhenStringContainsWithoutComparisonRunsAfterToList()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main(string search)
                {
                    var db = new DbContext();
                    var query = {|#0:db.Users.ToList().Where(x => x.Name.Contains(search))|};
                }
            }
            """ + MockTypes;

        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("Where");

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
    public async Task DoesNotReport_Redundant_AsEnumerableThenToList()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var users = db.Users.AsEnumerable().ToList();
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_Redundant_LocalMaterializedThenToList()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var materialized = db.Users.ToList();
                    var users = materialized.ToList();
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_Redundant_ToHashSetThenToList()
    {
        // ToHashSet() de-duplicates; the following ToList() is NOT redundant — removing the set
        // (as the redundant fix would) silently drops de-duplication. So the analyzer must stay
        // quiet rather than report a "redundant" materialization and offer an unsafe collapse.
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var users = db.Users.ToHashSet().ToList();
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_Redundant_ToHashSetThenToArray()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var users = db.Users.ToHashSet().ToArray();
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_Redundant_ToImmutableHashSetThenToList()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var users = db.Users.ToImmutableHashSet().ToList();
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_Redundant_ComparerSetThenSet()
    {
        // A set source with a custom comparer is not redundant even when followed by another set:
        // collapsing would drop the comparer and change which duplicates are removed
        // (ToHashSet(OrdinalIgnoreCase) de-dups case-insensitively; a default ToHashSet would not).
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var names = db.Users.Select(u => u.Name).ToHashSet(StringComparer.OrdinalIgnoreCase).ToHashSet();
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_Redundant_ToDictionaryThenToList()
    {
        // ToDictionary() produces a Dictionary<,>; the trailing ToList() yields List<KeyValuePair<,>>,
        // a genuine shape change, not a redundant re-materialization. Reporting "ToList redundant
        // because ToDictionary" would be misleading, so the analyzer stays quiet.
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var pairs = db.Users.ToDictionary(u => u.Id).ToList();
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_Redundant_ToLookupThenToList()
    {
        // ToLookup() produces an ILookup<,>; the trailing ToList() yields List<IGrouping<,>>,
        // a genuine shape change, not a redundant re-materialization.
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var groups = db.Users.ToLookup(u => u.Age).ToList();
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
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

    [Fact]
    public async Task DoesNotReport_WhenContinuationUsesLocalMethod()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var filtered = db.Users.ToList().Where(x => IsActive(x));
                }

                private static bool IsActive(User user) => user.Age > 18;
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_WhenContinuationUsesRegex()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main(string pattern)
                {
                    var db = new DbContext();
                    var filtered = db.Users.ToList().Where(x => System.Text.RegularExpressions.Regex.IsMatch(x.Name, pattern));
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_WhenContinuationUsesDelegatePredicate()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main(Func<User, bool> predicate)
                {
                    var db = new DbContext();
                    var filtered = db.Users.ToList().Where(predicate);
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_WhenStringComparisonOverloadRunsAfterToList()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main(string search)
                {
                    var db = new DbContext();
                    var filtered = db.Users.ToList().Where(x => x.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
