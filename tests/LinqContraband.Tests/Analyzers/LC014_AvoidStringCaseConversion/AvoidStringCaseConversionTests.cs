using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC014_AvoidStringCaseConversion.AvoidStringCaseConversionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC014_AvoidStringCaseConversion;

public class AvoidStringCaseConversionTests
{
    private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
";

    private const string TestClasses = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public void Dispose() {}
        public DbSet<T> Set<T>() where T : class => null;
    }

    public class DbSet<T> : IQueryable<T> where T : class
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }
}

namespace LinqContraband.Test
{
    public class User
    {
        public string Name { get; set; }
        public Address Address { get; set; }
    }

    public class Address
    {
        public string City { get; set; }
    }

    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<User> UserAliases { get; set; }
    }
}
";

    [Fact]
    public async Task ToLower_InWhereClause_ShouldTrigger()
    {
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var query = db.Users;
            var result = query.Where(u => {|LC014:u.Name.ToLower()|} == ""test"");
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ToLower_OnIQueryableAliasFromDbSet_ShouldTrigger()
    {
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            IQueryable<User> query = db.Users;
            var result = query.Where(u => {|LC014:u.Name.ToLower()|} == ""test"");
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ToUpper_InWhereClause_ShouldTrigger()
    {
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var query = db.Users;
            var result = query.Where(u => {|LC014:u.Name.ToUpper()|} == ""test"");
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ToLowerInvariant_InOrderBy_ShouldTrigger()
    {
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var query = db.Users;
            var result = query.OrderBy(u => {|LC014:u.Name.ToLowerInvariant()|});
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedProperty_ToLower_ShouldTrigger()
    {
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var query = db.Users;
            var result = query.Where(u => {|LC014:u.Address.City.ToLower()|} == ""ny"");
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConstantToLower_ShouldNotTrigger()
    {
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var query = db.Users;
            // ""test"".ToLower() is a constant expression, not on the column.
            var result = query.Where(u => u.Name == ""TEST"".ToLower());
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LocalVariableToLower_ShouldNotTrigger()
    {
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var search = ""TEST"";
            using var db = new AppDbContext();
            var query = db.Users;
            // search.ToLower() executes on client before query or as constant.
            var result = query.Where(u => u.Name == search.ToLower());
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EnumerableWhere_ShouldNotTrigger()
    {
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var list = new List<User>();
            // Enumerable.Where executes in memory, so index usage is irrelevant.
            var result = list.Where(u => u.Name.ToLower() == ""test"");
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LinqToObjectsAsQueryable_ShouldNotTrigger()
    {
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<User>().AsQueryable();
            var result = query.Where(u => u.Name.ToLower() == ""test"");
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    /*
    // Null propagation is not supported in Expression Trees by the C# compiler (CS8072),
    // so this code is technically impossible to write for IQueryable.
    [Fact]
    public async Task NullPropagation_ShouldTrigger()
    {
        // ...
    }
    */

    [Fact]
    public async Task Coalesce_ShouldTrigger()
    {
        // Failing Case 2: Coalesce operator
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var query = db.Users;
            var result = query.Where(u => {|LC014:(u.Name ?? """").ToLower()|} == ""test"");
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedHelperCall_ShouldTrigger()
    {
        // Failing Case 3: Nested inside another method call
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public bool MyHelper(string s) => true;

        public void TestMethod()
        {
            using var db = new AppDbContext();
            var query = db.Users;
            // The ToLower is an argument to MyHelper, which is the argument to Where
            var result = query.Where(u => MyHelper({|LC014:u.Name.ToLower()|}));
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Join_OuterEfKeySelector_ShouldTrigger()
    {
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var aliases = new List<User>();

            var result = db.Users.Join(
                aliases,
                u => {|LC014:u.Name.ToLower()|},
                alias => alias.Name,
                (u, alias) => u);
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Join_InMemoryInnerKeySelector_ShouldNotTrigger()
    {
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var aliases = new List<User>();

            var result = db.Users.Join(
                aliases,
                u => u.Name,
                alias => alias.Name.ToLower(),
                (u, alias) => u);
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Join_InnerEfKeySelector_ShouldTrigger()
    {
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();

            var result = db.Users.Join(
                db.UserAliases,
                u => u.Name,
                alias => {|LC014:alias.Name.ToLower()|},
                (u, alias) => u);
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Join_ResultSelectorProjection_ShouldNotTrigger()
    {
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();

            var result = db.Users.Join(
                db.UserAliases,
                u => u.Name,
                alias => alias.Name,
                (u, alias) => u.Name.ToLower());
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
