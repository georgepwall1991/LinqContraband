using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer,
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer,
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingFixer>;

namespace LinqContraband.Tests.Analyzers.LC009_MissingAsNoTracking;

public class MissingAsNoTrackingFixerTests
{
    // Helper to add references if needed

    [Fact]
    public async Task FixCrime_InjectsAsNoTracking()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore { 
    public static class EntityFrameworkQueryableExtensions {
        public static System.Linq.IQueryable<T> AsNoTracking<T>(this System.Linq.IQueryable<T> source) => source;
    }
    public class DbSet<T> : System.Linq.IQueryable<T> // Mock DbSet for test
    {
        public System.Type ElementType => throw new System.NotImplementedException();
        public System.Linq.Expressions.Expression Expression => throw new System.NotImplementedException();
        public System.Linq.IQueryProvider Provider => throw new System.NotImplementedException();
        public System.Collections.Generic.IEnumerator<T> GetEnumerator() => throw new System.NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new System.NotImplementedException();
    }
} // Fake namespace to avoid CS0234

class DbContext { public Microsoft.EntityFrameworkCore.DbSet<User> Users => null; }
class User { }

class Test
{
    void Run()
    {
        var db = new DbContext();
        var q = {|LC009:db.Users.Where(u => u != null).ToList()|};
    }
}";

        var fix = @"
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore { 
    public static class EntityFrameworkQueryableExtensions {
        public static System.Linq.IQueryable<T> AsNoTracking<T>(this System.Linq.IQueryable<T> source) => source;
    }
    public class DbSet<T> : System.Linq.IQueryable<T> // Mock DbSet for test
    {
        public System.Type ElementType => throw new System.NotImplementedException();
        public System.Linq.Expressions.Expression Expression => throw new System.NotImplementedException();
        public System.Linq.IQueryProvider Provider => throw new System.NotImplementedException();
        public System.Collections.Generic.IEnumerator<T> GetEnumerator() => throw new System.NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new System.NotImplementedException();
    }
} // Fake namespace to avoid CS0234

class DbContext { public Microsoft.EntityFrameworkCore.DbSet<User> Users => null; }
class User { }

class Test
{
    void Run()
    {
        var db = new DbContext();
        var q = db.Users.AsNoTracking().Where(u => u != null).ToList();
    }
}";
        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task FixCrime_InjectsAsNoTracking_WithMissingUsing()
    {
        // Test case where "using Microsoft.EntityFrameworkCore;" is MISSING.
        // This triggers the EnsureUsing path which was causing the crash.
        var test = @"
using System.Linq;
using System.Collections.Generic;

namespace Microsoft.EntityFrameworkCore { 
    public static class EntityFrameworkQueryableExtensions {
        public static System.Linq.IQueryable<T> AsNoTracking<T>(this System.Linq.IQueryable<T> source) => source;
    }
    public class DbSet<T> : System.Linq.IQueryable<T> // Mock DbSet for test
    {
        public System.Type ElementType => throw new System.NotImplementedException();
        public System.Linq.Expressions.Expression Expression => throw new System.NotImplementedException();
        public System.Linq.IQueryProvider Provider => throw new System.NotImplementedException();
        public System.Collections.Generic.IEnumerator<T> GetEnumerator() => throw new System.NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new System.NotImplementedException();
    }
} // Fake namespace to allow the using to be valid

class DbContext { public Microsoft.EntityFrameworkCore.DbSet<User> Users => null; }
class User { }

class Test
{
    void Run()
    {
        var db = new DbContext();
        var q = {|LC009:db.Users.Where(u => u != null).ToList()|};
    }
}";

        var fix = @"
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore { 
    public static class EntityFrameworkQueryableExtensions {
        public static System.Linq.IQueryable<T> AsNoTracking<T>(this System.Linq.IQueryable<T> source) => source;
    }
    public class DbSet<T> : System.Linq.IQueryable<T> // Mock DbSet for test
    {
        public System.Type ElementType => throw new System.NotImplementedException();
        public System.Linq.Expressions.Expression Expression => throw new System.NotImplementedException();
        public System.Linq.IQueryProvider Provider => throw new System.NotImplementedException();
        public System.Collections.Generic.IEnumerator<T> GetEnumerator() => throw new System.NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new System.NotImplementedException();
    }
} // Fake namespace to allow the using to be valid

class DbContext { public Microsoft.EntityFrameworkCore.DbSet<User> Users => null; }
class User { }

class Test
{
    void Run()
    {
        var db = new DbContext();
        var q = db.Users.AsNoTracking().Where(u => u != null).ToList();
    }
}";
        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task FixCrime_InjectsAsNoTracking_OnSetGenericSource()
    {
        // For a context.Set<User>() source, AsNoTracking() must wrap the Set<User>() call —
        // NOT the DbContext (db.AsNoTracking() does not exist and would not compile).
        var test = @"
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore {
    public static class EntityFrameworkQueryableExtensions {
        public static System.Linq.IQueryable<T> AsNoTracking<T>(this System.Linq.IQueryable<T> source) => source;
    }
    public class DbSet<T> : System.Linq.IQueryable<T> // Mock DbSet for test
    {
        public System.Type ElementType => throw new System.NotImplementedException();
        public System.Linq.Expressions.Expression Expression => throw new System.NotImplementedException();
        public System.Linq.IQueryProvider Provider => throw new System.NotImplementedException();
        public System.Collections.Generic.IEnumerator<T> GetEnumerator() => throw new System.NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new System.NotImplementedException();
    }
} // Fake namespace to avoid CS0234

class DbContext { public Microsoft.EntityFrameworkCore.DbSet<T> Set<T>() where T : class => null; }
class User { }

class Test
{
    void Run()
    {
        var db = new DbContext();
        var q = {|LC009:db.Set<User>().Where(u => u != null).ToList()|};
    }
}";

        var fix = @"
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore {
    public static class EntityFrameworkQueryableExtensions {
        public static System.Linq.IQueryable<T> AsNoTracking<T>(this System.Linq.IQueryable<T> source) => source;
    }
    public class DbSet<T> : System.Linq.IQueryable<T> // Mock DbSet for test
    {
        public System.Type ElementType => throw new System.NotImplementedException();
        public System.Linq.Expressions.Expression Expression => throw new System.NotImplementedException();
        public System.Linq.IQueryProvider Provider => throw new System.NotImplementedException();
        public System.Collections.Generic.IEnumerator<T> GetEnumerator() => throw new System.NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new System.NotImplementedException();
    }
} // Fake namespace to avoid CS0234

class DbContext { public Microsoft.EntityFrameworkCore.DbSet<T> Set<T>() where T : class => null; }
class User { }

class Test
{
    void Run()
    {
        var db = new DbContext();
        var q = db.Set<User>().AsNoTracking().Where(u => u != null).ToList();
    }
}";
        await VerifyCS.VerifyCodeFixAsync(test, fix);
    }

    [Fact]
    public async Task FixAll_RewritesAllMissingAsNoTrackingCases()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore {
    public static class EntityFrameworkQueryableExtensions {
        public static System.Linq.IQueryable<T> AsNoTracking<T>(this System.Linq.IQueryable<T> source) => source;
    }
    public class DbSet<T> : System.Linq.IQueryable<T> // Mock DbSet for test
    {
        public System.Type ElementType => throw new System.NotImplementedException();
        public System.Linq.Expressions.Expression Expression => throw new System.NotImplementedException();
        public System.Linq.IQueryProvider Provider => throw new System.NotImplementedException();
        public System.Collections.Generic.IEnumerator<T> GetEnumerator() => throw new System.NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new System.NotImplementedException();
    }
} // Fake namespace to avoid CS0234

class DbContext { public Microsoft.EntityFrameworkCore.DbSet<User> Users => null; }
class User { }

class Test
{
    void GetActiveUsers()
    {
        var db = new DbContext();
        var q1 = {|#0:db.Users.Where(u => u != null).ToList()|};
    }

    void GetAllUsers()
    {
        var db = new DbContext();
        var q2 = {|#1:db.Users.Where(u => u == null).ToList()|};
    }
}";

        var fixedCode = @"
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore {
    public static class EntityFrameworkQueryableExtensions {
        public static System.Linq.IQueryable<T> AsNoTracking<T>(this System.Linq.IQueryable<T> source) => source;
    }
    public class DbSet<T> : System.Linq.IQueryable<T> // Mock DbSet for test
    {
        public System.Type ElementType => throw new System.NotImplementedException();
        public System.Linq.Expressions.Expression Expression => throw new System.NotImplementedException();
        public System.Linq.IQueryProvider Provider => throw new System.NotImplementedException();
        public System.Collections.Generic.IEnumerator<T> GetEnumerator() => throw new System.NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new System.NotImplementedException();
    }
} // Fake namespace to avoid CS0234

class DbContext { public Microsoft.EntityFrameworkCore.DbSet<User> Users => null; }
class User { }

class Test
{
    void GetActiveUsers()
    {
        var db = new DbContext();
        var q1 = db.Users.AsNoTracking().Where(u => u != null).ToList();
    }

    void GetAllUsers()
    {
        var db = new DbContext();
        var q2 = db.Users.AsNoTracking().Where(u => u == null).ToList();
    }
}";

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "AddAsNoTracking"
        };

        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC009", DiagnosticSeverity.Info)
                .WithArguments("GetActiveUsers")
                .WithLocation(0));
        testObj.ExpectedDiagnostics.Add(
            new DiagnosticResult("LC009", DiagnosticSeverity.Info)
                .WithArguments("GetAllUsers")
                .WithLocation(1));

        await testObj.RunAsync();
    }
}
