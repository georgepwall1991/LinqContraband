using LinqContraband.Analyzers.LC002_PrematureMaterialization;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC002_PrematureMaterialization;

public class PrematureMaterializationEdgeCasesTests
{
    private const string CommonUsings = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
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
        }
        """;

    [Fact]
    public async Task DoesNotReport_WhenLocalAliasesSingleAssignmentMaterialization()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var materialized = db.Users.ToList();
                    var alias = materialized;
                    var filtered = alias.Where(x => x.Age > 18);
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_WhenMaterializingConstructorFeedsCount()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var materialized = new HashSet<User>(db.Users);
                    var count = materialized.Count();
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_WhenLocalHasAmbiguousAssignments()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main(bool chooseQuery)
                {
                    IEnumerable<User> users;
                    if (chooseQuery)
                    {
                        var db = new DbContext();
                        users = db.Users.ToList();
                    }
                    else
                    {
                        users = new List<User>();
                    }

                    var filtered = users.Where(x => x.Age > 18);
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_WhenMaterializationLivesInField()
    {
        var test = CommonUsings + """

            class Program
            {
                private readonly IEnumerable<User> _users;

                public Program(DbContext db)
                {
                    _users = db.Users.ToList();
                }

                void Main()
                {
                    var filtered = _users.Where(x => x.Age > 18);
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_ForIndexAwareWhereOverload()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var filtered = db.Users.ToList().Where((x, index) => index > 0);
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_ForComparerSensitiveDistinctOverload()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var filtered = db.Users.Select(x => x.Name).ToList().Distinct(StringComparer.OrdinalIgnoreCase);
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_ForDistinctEvenWithoutComparer()
    {
        var test = CommonUsings + """

            class Program
            {
                void Main()
                {
                    var db = new DbContext();
                    var filtered = db.Users.ToList().Distinct();
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DoesNotReport_ForUnsupportedProjectionThroughProperty()
    {
        var test = CommonUsings + """

            class Program
            {
                IEnumerable<User> MaterializedUsers { get; }

                public Program(DbContext db)
                {
                    MaterializedUsers = db.Users.ToList();
                }

                void Main()
                {
                    var filtered = MaterializedUsers.Where(x => x.Age > 18);
                }
            }
            """ + MockTypes;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
