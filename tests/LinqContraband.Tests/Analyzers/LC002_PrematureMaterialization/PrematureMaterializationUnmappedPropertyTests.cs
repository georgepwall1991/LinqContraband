using LinqContraband.Analyzers.LC002_PrematureMaterialization;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC002_PrematureMaterialization.PrematureMaterializationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC002_PrematureMaterialization;

// A computed or [NotMapped] property only exists on the client. Materializing before filtering on it is how
// the query has to be written, and moving the filter into SQL throws "could not be translated".
public class PrematureMaterializationUnmappedPropertyTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.ComponentModel.DataAnnotations.Schema;
        using System.Linq;

        namespace TestNamespace
        {
            public class User
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public string Display => Name + "!";
                public string Slug { get { return Name.ToLower(); } }
                public string Code { get; } = "";
                [NotMapped] public string Nickname { get; set; } = "";
                public List<string> Tags { get; set; } = new();
            }

            public class Settings
            {
                public string Name { get; set; } = "";
                public string Display => Name + "!";
            }

            public class DbContext
            {
                public IQueryable<User> Users => new List<User>().AsQueryable();
            }

            class Program
            {
                void Run(DbContext db, Settings settings)
                {
        BODY
                }
            }
        }

        namespace System.ComponentModel.DataAnnotations.Schema
        {
            public sealed class NotMappedAttribute : Attribute { }
        }
        """;

    [Theory]
    [InlineData("var users = db.Users.ToList().Where(u => u.Display == \"ann!\").ToList();")]
    [InlineData("var users = db.Users.ToList().Where(u => u.Slug == \"ann\").ToList();")]
    [InlineData("var users = db.Users.ToList().Where(u => u.Code == \"a\").ToList();")]
    [InlineData("var users = db.Users.ToList().Where(u => u.Nickname == \"ann\").ToList();")]
    [InlineData("var count = db.Users.AsEnumerable().Count(u => u.Display.Length > 3);")]
    [InlineData("var users = db.Users.ToList().Where(u => u.Tags[0] == \"a\").ToList();")]
    public async Task ClientOnlyPropertyOfTheRow_DoesNotReport(string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(Source.Replace("BODY", body));
    }

    [Theory]
    [InlineData("var users = {|#0:db.Users.ToList().Where(u => u.Name == \"ann\")|};")]
    [InlineData("var users = {|#0:db.Users.ToList().Where(u => u.Name.Length > 3)|};")]
    // A computed property of a captured object is evaluated once and sent as a parameter.
    [InlineData("var users = {|#0:db.Users.ToList().Where(u => u.Name == settings.Display)|};")]
    public async Task MappedPropertyOfTheRow_StillReports(string body)
    {
        var expected = VerifyCS.Diagnostic(PrematureMaterializationAnalyzer.Rule)
            .WithLocation(0)
            .WithArguments("Where");

        await VerifyCS.VerifyAnalyzerAsync(Source.Replace("BODY", body), expected);
    }
}
