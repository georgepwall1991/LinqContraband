using System.Threading.Tasks;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC016_AvoidDateTimeNow.AvoidDateTimeNowAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC016_AvoidDateTimeNow;

public class AvoidDateTimeNowTests
{
    [Fact]
    public async Task Diagnostic_DateTimeNow_InWhere()
    {
        var test = @"
using System;
using System.Linq;
using System.Collections.Generic;

class User { public DateTime Dob { get; set; } }

class Test
{
    void Method(IQueryable<User> users)
    {
        var q = users.Where(u => u.Dob < {|LC016:DateTime.Now|});
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Diagnostic_DateTimeUtcNow_InWhere()
    {
        var test = @"
using System;
using System.Linq;
using System.Collections.Generic;

class User { public DateTime Dob { get; set; } }

class Test
{
    void Method(IQueryable<User> users)
    {
        var q = users.Where(u => u.Dob < {|LC016:DateTime.UtcNow|});
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Diagnostic_DateTimeOffsetNow_InWhere()
    {
        var test = @"
using System;
using System.Linq;
using System.Collections.Generic;

class User { public DateTimeOffset CreatedAt { get; set; } }

class Test
{
    void Method(IQueryable<User> users)
    {
        var q = users.Where(u => u.CreatedAt < {|LC016:DateTimeOffset.Now|});
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NoDiagnostic_LocalVariable()
    {
        var test = @"
using System;
using System.Linq;
using System.Collections.Generic;

class User { public DateTime Dob { get; set; } }

class Test
{
    void Method(IQueryable<User> users)
    {
        var now = DateTime.Now;
        var q = users.Where(u => u.Dob < now);
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NoDiagnostic_IEnumerable()
    {
        var test = @"
using System;
using System.Linq;
using System.Collections.Generic;

class User { public DateTime Dob { get; set; } }

class Test
{
    void Method(IEnumerable<User> users)
    {
        // Enumerable.Where is fine (in-memory)
        var q = users.Where(u => u.Dob < DateTime.Now);
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Diagnostic_NestedQuery()
    {
        var test = @"
using System;
using System.Linq;
using System.Collections.Generic;

class User { public DateTime Dob { get; set; } }

class Test
{
    void Method(IQueryable<User> users)
    {
        // Any is a target method
        var q = users.Any(u => u.Dob > {|LC016:DateTime.UtcNow|});
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
