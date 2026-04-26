using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC022_ToListInSelectProjection.ToListInSelectProjectionAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC022_ToListInSelectProjection.ToListInSelectProjectionAnalyzer,
    LinqContraband.Analyzers.LC022_ToListInSelectProjection.ToListInSelectProjectionFixer>;

namespace LinqContraband.Tests.Analyzers.LC022_ToListInSelectProjection;

public class ToListInSelectProjectionTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using TestApp;
";

    private const string MockNamespaces = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }
}

namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<Order> Orders { get; set; }
    }
    public class Order { public int Id { get; set; } }
}
";

    [Fact]
    public async Task Select_WithToList_ShouldTriggerLC022()
    {
        var test = Usings + @"
class TestClass
{
    void TestMethod(DbSet<User> users)
    {
        var result = users.Select(u => {|LC022:u.Orders.ToList()|});
    }
}" + MockNamespaces;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Select_WithToArray_ShouldTriggerLC022()
    {
        var test = Usings + @"
class TestClass
{
    void TestMethod(DbSet<User> users)
    {
        var result = users.Select(u => {|LC022:u.Orders.ToArray()|});
    }
}" + MockNamespaces;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task QuerySyntaxSelect_WithToList_ShouldTriggerLC022()
    {
        var test = Usings + @"
class TestClass
{
    void TestMethod(DbSet<User> users)
    {
        var result = from u in users
                     select {|LC022:u.Orders.ToList()|};
    }
}" + MockNamespaces;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBy_Select_WithToList_ShouldNotTriggerLC022()
    {
        var test = Usings + @"
class TestClass
{
    void TestMethod(DbSet<User> users)
    {
        var result = users
            .GroupBy(u => u.Id)
            .Select(g => g.ToList());
    }
}" + MockNamespaces;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Select_WithToListInsideAnonymousType_ShouldTriggerLC022()
    {
        var test = Usings + @"
class TestClass
{
    void TestMethod(DbSet<User> users)
    {
        var result = users.Select(u => new { Items = {|LC022:u.Orders.ToList()|} });
    }
}" + MockNamespaces;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupBySelect_WithGroupingToList_ShouldNotTriggerLC022()
    {
        var test = Usings + @"
class TestClass
{
    void TestMethod(IQueryable<Order> orders)
    {
        var result = orders
            .GroupBy(o => o.Id)
            .Select(g => g.ToList());
    }
}" + MockNamespaces;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ToList_OutsideSelect_ShouldNotTrigger()
    {
        var test = Usings + @"
class TestClass
{
    void TestMethod(DbSet<User> users)
    {
        var result = users.ToList();
    }
}" + MockNamespaces;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Select_OnList_ShouldNotTrigger()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;

class Item { public List<string> Tags { get; set; } }

class TestClass
{
    void TestMethod(List<Item> items)
    {
        var result = items.Select(i => i.Tags.ToList());
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Select_WithUnrelatedMaterializerInsideLambda_ShouldNotTrigger()
    {
        var test = Usings + @"
class TestClass
{
    void TestMethod(DbSet<User> users)
    {
        var result = users.Select(u => new List<int>().ToList());
    }
}" + MockNamespaces;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRemoveToArray_WhenTypesWouldChange()
    {
        var test = Usings + @"
class TestClass
{
    void TestMethod(DbSet<User> users)
    {
        var result = users.Select(u => {|LC022:u.Orders.ToArray()|});
    }
}" + MockNamespaces;

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldRemoveToList()
    {
        var test = Usings + @"
class TestClass
{
    void TestMethod(DbSet<User> users)
    {
        var result = users.Select(u => {|LC022:u.Orders.ToList()|});
    }
}" + MockNamespaces;

        var fixedCode = Usings + @"
class TestClass
{
    void TestMethod(DbSet<User> users)
    {
        var result = users.Select(u => u.Orders);
    }
}" + MockNamespaces;

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldNotRemoveToList_InsideObjectInitializer()
    {
        var test = Usings + @"
class UserDto
{
    public List<Order> Items { get; set; }
}

class TestClass
{
    void TestMethod(DbSet<User> users)
    {
        var result = users.Select(u => new UserDto { Items = {|LC022:u.Orders.ToList()|} });
    }
}" + MockNamespaces;

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRemoveToList_WhenUsedAsMethodArgument()
    {
        var test = Usings + @"
class TestClass
{
    void TestMethod(DbSet<User> users)
    {
        var result = users.Select(u => ConvertOrders({|LC022:u.Orders.ToList()|}));
    }

    static string ConvertOrders(List<Order> orders) => orders.Count.ToString();
}" + MockNamespaces;

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldNotRemoveToList_WhenReceiverTypeDiffersFromMaterializedType()
    {
        var test = Usings + @"
class TestClass
{
    void TestMethod(DbSet<User> users)
    {
        var result = users.Select(u => {|LC022:u.Name.ToList()|});
    }
}" + MockNamespaces;

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }
}
