using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC014_AvoidStringCaseConversion.AvoidStringCaseConversionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC014_AvoidStringCaseConversion;

public partial class AvoidStringCaseConversionTests
{
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
        // case conversion is column-derived and must fire - the value-type skip must unwrap
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
