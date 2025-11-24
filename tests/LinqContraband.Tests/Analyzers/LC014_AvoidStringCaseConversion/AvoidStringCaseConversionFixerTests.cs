using System.Threading.Tasks;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC014_AvoidStringCaseConversion.AvoidStringCaseConversionAnalyzer,
    LinqContraband.Analyzers.LC014_AvoidStringCaseConversion.AvoidStringCaseConversionFixer>;

namespace LinqContraband.Tests.Analyzers.LC014_AvoidStringCaseConversion;

public class AvoidStringCaseConversionFixerTests
{
    [Fact]
    public async Task FixToLowerEquality()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;

class User { public string Name { get; set; } }

class Test
{
    void Method(IQueryable<User> users)
    {
        var q = users.Where(u => {|LC014:u.Name.ToLower()|} == ""john"");
    }
}";

        var fix = @"
using System.Linq;
using System.Collections.Generic;
using System;

class User { public string Name { get; set; } }

class Test
{
    void Method(IQueryable<User> users)
    {
        var q = users.Where(u => string.Equals(u.Name, ""john"", StringComparison.OrdinalIgnoreCase));
    }
}";
        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task FixToUpperEquality_ReverseOrder()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;

class User { public string Name { get; set; } }

class Test
{
    void Method(IQueryable<User> users)
    {
        var q = users.Where(u => ""JOHN"" == {|LC014:u.Name.ToUpper()|});
    }
}";

        var fix = @"
using System.Linq;
using System.Collections.Generic;
using System;

class User { public string Name { get; set; } }

class Test
{
    void Method(IQueryable<User> users)
    {
        var q = users.Where(u => string.Equals(u.Name, ""JOHN"", StringComparison.OrdinalIgnoreCase));
    }
}";
        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task FixToLowerInequality()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;

class User { public string Name { get; set; } }

class Test
{
    void Method(IQueryable<User> users)
    {
        var q = users.Where(u => {|LC014:u.Name.ToLower()|} != ""john"");
    }
}";

        var fix = @"
using System.Linq;
using System.Collections.Generic;
using System;

class User { public string Name { get; set; } }

class Test
{
    void Method(IQueryable<User> users)
    {
        var q = users.Where(u => !string.Equals(u.Name, ""john"", StringComparison.OrdinalIgnoreCase));
    }
}";
        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }
    
    [Fact]
    public async Task FixToLowerEqualsMethod()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;

class User { public string Name { get; set; } }

class Test
{
    void Method(IQueryable<User> users)
    {
        var q = users.Where(u => {|LC014:u.Name.ToLower()|}.Equals(""john""));
    }
}";

        var fix = @"
using System.Linq;
using System.Collections.Generic;
using System;

class User { public string Name { get; set; } }

class Test
{
    void Method(IQueryable<User> users)
    {
        var q = users.Where(u => string.Equals(u.Name, ""john"", StringComparison.OrdinalIgnoreCase));
    }
}";
        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task FixWithVariableComparison()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;

class User { public string Name { get; set; } }

class Test
{
    void Method(IQueryable<User> users, string searchTerm)
    {
        var q = users.Where(u => {|LC014:u.Name.ToLower()|} == searchTerm.ToLower());
    }
}";

        var fix = @"
using System.Linq;
using System.Collections.Generic;
using System;

class User { public string Name { get; set; } }

class Test
{
    void Method(IQueryable<User> users, string searchTerm)
    {
        var q = users.Where(u => string.Equals(u.Name, searchTerm.ToLower(), StringComparison.OrdinalIgnoreCase));
    }
}";
        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task FixNestedProperty()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;

class Address { public string City { get; set; } }
class User { public Address Addr { get; set; } }

class Test
{
    void Method(IQueryable<User> users)
    {
        var q = users.Where(u => {|LC014:u.Addr.City.ToLower()|} == ""london"");
    }
}";

        var fix = @"
using System.Linq;
using System.Collections.Generic;
using System;

class Address { public string City { get; set; } }
class User { public Address Addr { get; set; } }

class Test
{
    void Method(IQueryable<User> users)
    {
        var q = users.Where(u => string.Equals(u.Addr.City, ""london"", StringComparison.OrdinalIgnoreCase));
    }
}";
        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task FixPreservesExistingSystemUsing()
    {
        var test = @"
using System;
using System.Linq;
using System.Collections.Generic;

class User { public string Name { get; set; } }

class Test
{
    void Method(IQueryable<User> users)
    {
        var q = users.Where(u => {|LC014:u.Name.ToLower()|} == ""john"");
    }
}";

        var fix = @"
using System;
using System.Linq;
using System.Collections.Generic;

class User { public string Name { get; set; } }

class Test
{
    void Method(IQueryable<User> users)
    {
        var q = users.Where(u => string.Equals(u.Name, ""john"", StringComparison.OrdinalIgnoreCase));
    }
}";
        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }
}