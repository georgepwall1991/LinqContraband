using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC001_LocalMethod;

/// <summary>
/// Leftover <c>TranslationCriticalQueryMethods</c> hosts the shipped fixtures do
/// not isolate. <c>Where</c>, static <c>ThenBy</c>, static <c>Join</c> and the
/// aggregate selectors <c>Sum</c>/<c>Average</c>/<c>Min</c>/<c>Max</c> already
/// have crime cases. Nested <c>Any</c> sits inside <c>Where</c>, so dropping
/// <c>Any</c> still reports through the outer filter. Each leftover name below
/// is the only translation-critical host of its local method. Dropping one
/// leftover name fails only that pin, except <c>OrderByDescending</c> which
/// also fails the lookalike-on-EF-receiver pin. An in-memory
/// <c>AsQueryable()</c> leftover host stays quiet. <c>Select</c> is a
/// documented non-goal and stays unpinned.
/// </summary>
public class LocalMethodLeftoverOperatorCoverageTests
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

    public class DbContext
    {
        public IQueryable<User> Users => new List<User>().AsQueryable();
    }
}";

    private static string Wrap(string body) => Usings + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        " + body + @"
    }

    bool IsAdult(int age) => age >= 18;
    int Key(int age) => age;
}
" + MockNamespace;

    [Fact]
    public async Task OrderBy_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var query = db.Users.OrderBy(u => {|LC001:Key(u.Age)|});"));
    }

    [Fact]
    public async Task OrderByDescending_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var query = db.Users.OrderByDescending(u => {|LC001:Key(u.Age)|});"));
    }

    [Fact]
    public async Task ThenByDescending_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var query = db.Users.OrderBy(u => u.Age).ThenByDescending(u => {|LC001:Key(u.Age)|});"));
    }

    [Fact]
    public async Task GroupJoin_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var query = db.Users.GroupJoin(db.Users,
            u => {|LC001:Key(u.Age)|},
            inner => inner.Age,
            (u, matches) => u);"));
    }

    [Fact]
    public async Task GroupBy_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var query = db.Users.GroupBy(u => {|LC001:Key(u.Age)|});"));
    }

    [Fact]
    public async Task Any_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var exists = db.Users.Any(u => {|LC001:IsAdult(u.Age)|});"));
    }

    [Fact]
    public async Task All_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var all = db.Users.All(u => {|LC001:IsAdult(u.Age)|});"));
    }

    [Fact]
    public async Task Count_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var n = db.Users.Count(u => {|LC001:IsAdult(u.Age)|});"));
    }

    [Fact]
    public async Task LongCount_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var n = db.Users.LongCount(u => {|LC001:IsAdult(u.Age)|});"));
    }

    [Fact]
    public async Task First_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var user = db.Users.First(u => {|LC001:IsAdult(u.Age)|});"));
    }

    [Fact]
    public async Task FirstOrDefault_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var user = db.Users.FirstOrDefault(u => {|LC001:IsAdult(u.Age)|});"));
    }

    [Fact]
    public async Task Single_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var user = db.Users.Single(u => {|LC001:IsAdult(u.Age)|});"));
    }

    [Fact]
    public async Task SingleOrDefault_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var user = db.Users.SingleOrDefault(u => {|LC001:IsAdult(u.Age)|});"));
    }

    [Fact]
    public async Task Last_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var user = db.Users.Last(u => {|LC001:IsAdult(u.Age)|});"));
    }

    [Fact]
    public async Task LastOrDefault_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var user = db.Users.LastOrDefault(u => {|LC001:IsAdult(u.Age)|});"));
    }

    [Fact]
    public async Task SkipWhile_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var query = db.Users.SkipWhile(u => {|LC001:IsAdult(u.Age)|});"));
    }

    [Fact]
    public async Task TakeWhile_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var query = db.Users.TakeWhile(u => {|LC001:IsAdult(u.Age)|});"));
    }

    [Fact]
    public async Task LookalikeOrderByDescending_OnEfReceiver_StillReports()
    {
        const string lookalike = @"
namespace OtherOrm
{
    public static class QueryExtensions
    {
        public static IQueryable<T> OrderByDescending<T, TKey>(
            this IQueryable<T> source,
            Expression<Func<T, TKey>> keySelector) => source;
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(Usings + lookalike + @"
class Program
{
    void Main()
    {
        var db = new DbContext();
        var query = OtherOrm.QueryExtensions.OrderByDescending(db.Users, u => {|LC001:Key(u.Age)|});
    }

    int Key(int age) => age;
}
" + MockNamespace);
    }

    [Fact]
    public async Task OrderByDescending_OnInMemoryAsQueryable_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(@"
using System;
using System.Linq;
using System.Collections.Generic;

class User { public int Age { get; set; } }

class Program
{
    void Main()
    {
        var users = new List<User>().AsQueryable();
        var query = users.OrderByDescending(u => Key(u.Age));
    }

    int Key(int age) => age;
}");
    }
}
