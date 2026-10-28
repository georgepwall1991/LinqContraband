using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC016_AvoidDateTimeNow.AvoidDateTimeNowAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC016_AvoidDateTimeNow;

/// <summary>
/// Leftover <c>TargetLinqMethods</c> names the shipped fixtures do not isolate.
/// <c>Where</c>, <c>Select</c>, <c>OrderBy</c>, <c>SkipWhile</c>, <c>Join</c>
/// and <c>Any</c> already have crime cases. Dropping a leftover operator name
/// fails only that operator's pin. An in-memory <c>AsQueryable()</c>
/// <c>OrderByDescending</c> stays quiet so a later edit that treats every
/// leftover operator as EF-scoped is visible. Query-syntax <c>orderby</c>
/// lowers to <c>OrderBy</c> and stays unpinned.
/// </summary>
public partial class AvoidDateTimeNowTests
{
    private static string Wrap(string body, bool includeOrder = false)
    {
        var extraParams = includeOrder
            ? ", IQueryable<Order> orders"
            : string.Empty;

        return $@"
using System;
using System.Linq;
using System.Collections.Generic;

class User {{ public DateTime Dob {{ get; set; }} public ICollection<Order> Orders {{ get; set; }} }}
class Order {{ public DateTime Date {{ get; set; }} }}

class Test
{{
    void Method(IQueryable<User> users{extraParams})
    {{
        {body}
    }}
}}";
    }

    [Fact]
    public async Task OrderByDescending_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var q = users.OrderByDescending(u => {|LC016:DateTime.Now|} - u.Dob);"));
    }

    [Fact]
    public async Task ThenBy_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var q = users.OrderBy(u => u.Dob).ThenBy(u => {|LC016:DateTime.Now|} - u.Dob);"));
    }

    [Fact]
    public async Task ThenByDescending_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var q = users.OrderBy(u => u.Dob).ThenByDescending(u => {|LC016:DateTime.Now|} - u.Dob);"));
    }

    [Fact]
    public async Task Count_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var n = users.Count(u => u.Dob < {|LC016:DateTime.Now|});"));
    }

    [Fact]
    public async Task LongCount_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var n = users.LongCount(u => u.Dob < {|LC016:DateTime.Now|});"));
    }

    [Fact]
    public async Task All_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var all = users.All(u => u.Dob < {|LC016:DateTime.Now|});"));
    }

    [Fact]
    public async Task First_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var user = users.First(u => u.Dob < {|LC016:DateTime.Now|});"));
    }

    [Fact]
    public async Task FirstOrDefault_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var user = users.FirstOrDefault(u => u.Dob < {|LC016:DateTime.Now|});"));
    }

    [Fact]
    public async Task Single_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var user = users.Single(u => u.Dob < {|LC016:DateTime.Now|});"));
    }

    [Fact]
    public async Task SingleOrDefault_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var user = users.SingleOrDefault(u => u.Dob < {|LC016:DateTime.Now|});"));
    }

    [Fact]
    public async Task Last_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var user = users.Last(u => u.Dob < {|LC016:DateTime.Now|});"));
    }

    [Fact]
    public async Task LastOrDefault_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var user = users.LastOrDefault(u => u.Dob < {|LC016:DateTime.Now|});"));
    }

    [Fact]
    public async Task TakeWhile_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var q = users.TakeWhile(u => u.Dob < {|LC016:DateTime.Now|});"));
    }

    [Fact]
    public async Task SelectMany_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var q = users.SelectMany(u => u.Orders, (u, o) => new { u, Now = {|LC016:DateTime.Now|} });", includeOrder: true));
    }

    [Fact]
    public async Task GroupJoin_OuterKeySelector_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(Wrap(@"
        var q = users.GroupJoin(orders,
            u => {|LC016:DateTime.Now|},
            o => o.Date,
            (u, matches) => u);", includeOrder: true));
    }

    [Fact]
    public async Task OrderByDescending_OnInMemoryAsQueryable_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(@"
using System;
using System.Linq;
using System.Collections.Generic;

class User { public DateTime Dob { get; set; } }

class Test
{
    void Method(List<User> users)
    {
        var q = users.AsQueryable().OrderByDescending(u => DateTime.Now - u.Dob);
    }
}");
    }
}
