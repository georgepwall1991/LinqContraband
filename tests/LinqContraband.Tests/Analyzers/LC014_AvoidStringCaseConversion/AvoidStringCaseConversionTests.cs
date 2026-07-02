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

    public static class EntityFrameworkQueryableExtensions
    {
        public static System.Threading.Tasks.Task<bool> AnyAsync<TSource>(
            this IQueryable<TSource> source,
            System.Linq.Expressions.Expression<Func<TSource, bool>> predicate) => null;
    }
}

namespace LinqContraband.Test
{
    public class User
    {
        public string Name { get; set; }
        public int Age { get; set; }
        public char? MiddleInitial { get; set; }
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
    public async Task ToLower_InEfAnyAsyncPredicate_ShouldTrigger()
    {
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public async System.Threading.Tasks.Task TestMethod()
        {
            using var db = new AppDbContext();
            var exists = await db.Users.AnyAsync(u => {|LC014:u.Name.ToLower()|} == ""test"");
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

    [Fact]
    public async Task ToLower_OnMethodResultWithParameterArgument_ShouldTrigger()
    {
        // The case conversion is applied to the result of string.Concat(u.Name, u.Address.City).
        // That value depends on the query parameter through the method ARGUMENTS (not the
        // receiver), so it is a column-derived value and ToLower defeats sargability.
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var result = db.Users.Where(u => {|LC014:string.Concat(u.Name, u.Address.City).ToLower()|} == ""x"");
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ToUpper_OnParamsArrayMethodResultWithParameterArgument_ShouldTrigger()
    {
        // params overloads (string.Join here) wrap the column references in an array creation
        // for the params parameter, so the dependence walk must descend into the array elements.
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var result = db.Users.Where(u => {|LC014:string.Join(""-"", u.Name, u.Address.City).ToUpper()|} == ""X"");
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ToLower_OnMethodResultWithConstantArguments_ShouldNotTrigger()
    {
        // string.Concat of constant arguments does not depend on the query parameter, so the
        // value is computed client-side and the rule must stay quiet.
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var result = db.Users.Where(u => u.Name == string.Concat(""a"", ""b"").ToLower());
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConstantReceiver_ColumnAsSubstringLength_ShouldNotTrigger()
    {
        // The lowercased string content comes entirely from the constant receiver; the column
        // (u.Age) only controls the substring length (a numeric argument). Casing never touches
        // a column, so the value is not column-derived and the rule must stay quiet.
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var result = db.Users.Where(u => ""HELLO_WORLD"".Substring(0, u.Age).ToLower() == ""x"");
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConstantReceiver_ColumnAsPadWidth_ShouldNotTrigger()
    {
        // u.Name.Length is an int argument controlling pad width only; the lowercased text is the
        // constant receiver, so the rule must stay quiet.
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var result = db.Users.Where(u => ""CONSTANT"".PadRight(u.Name.Length).ToLower() == ""x"");
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConstantReceiver_ColumnAsRemoveIndex_ShouldNotTrigger()
    {
        // u.Age is an int index argument to Remove; the lowercased text is the constant receiver.
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var result = db.Users.Where(u => ""HELLOWORLD"".Remove(u.Age).ToLower() == ""x"");
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConstantReceiver_ColumnCharArgumentInReplace_ShouldTrigger()
    {
        // A char argument is a value type but DOES carry content into the result: u.Name[0]
        // (a column-derived char) becomes part of the replaced string, so the case conversion
        // touches column text and the rule must fire.
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var result = db.Users.Where(u => {|LC014:""xx"".Replace('x', u.Name[0]).ToLower()|} == ""x"");
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ColumnCharArgumentInConcat_ShouldTrigger()
    {
        // string.Concat over a column-derived char: the char contributes the result's content,
        // so the case conversion is column-derived and must fire (the value-type skip exempts char).
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var result = db.Users.Where(u => {|LC014:string.Concat(u.Name[0]).ToUpper()|} == ""X"");
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ColumnNullableCharArgumentInConcat_ShouldTrigger()
    {
        // A nullable char (char?) column still carries character content into the result, so the
        // case conversion is column-derived and must fire — the value-type skip must unwrap
        // Nullable<char> rather than treat it like a numeric positional argument.
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var result = db.Users.Where(u => {|LC014:string.Concat(u.MiddleInitial).ToLower()|} == ""x"");
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConstantReceiver_ColumnAsStringArgument_ShouldTrigger()
    {
        // Distinct from the numeric-argument cases above: here the column (u.Name) flows into the
        // result as a STRING argument to Replace, so the lowercased value carries column text and
        // the rule must still fire even though the receiver is a constant.
        var test = Usings + TestClasses + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            using var db = new AppDbContext();
            var result = db.Users.Where(u => {|LC014:""prefix"".Replace(""x"", u.Name).ToLower()|} == ""x"");
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
