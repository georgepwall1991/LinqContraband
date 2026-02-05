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
    public async Task Fixer_ShouldRemoveToArray()
    {
        var test = Usings + @"
class TestClass
{
    void TestMethod(DbSet<User> users)
    {
        var result = users.Select(u => {|LC022:u.Orders.ToArray()|});
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
}
