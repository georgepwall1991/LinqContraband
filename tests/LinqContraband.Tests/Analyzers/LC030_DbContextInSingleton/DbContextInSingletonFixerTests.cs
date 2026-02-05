using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC030_DbContextInSingleton.DbContextInSingletonAnalyzer,
    LinqContraband.Analyzers.LC030_DbContextInSingleton.DbContextInSingletonFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;

namespace LinqContraband.Tests.Analyzers.LC030_DbContextInSingleton;

public class DbContextInSingletonFixerTests
{
    private const string EFCoreMock = @"
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext { }
    public interface IDbContextFactory<TContext> where TContext : DbContext
    {
        TContext CreateDbContext();
    }
}
";

    [Fact]
    public async Task Fixer_ShouldChangeFieldTypeToFactory()
    {
        var test = EFCoreMock + @"
public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext { }

public class MyService
{
    private readonly AppDbContext {|LC030:_db|};
}
";

        var fixedCode = EFCoreMock + @"
public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext { }

public class MyService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
}
";

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldChangeFieldAndConstructorParameter()
    {
        var test = EFCoreMock + @"
public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext { }

public class MyService
{
    private readonly AppDbContext {|LC030:_db|};

    public MyService(AppDbContext db)
    {
    }
}
";

        var fixedCode = EFCoreMock + @"
public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext { }

public class MyService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public MyService(IDbContextFactory<AppDbContext> dbFactory)
    {
    }
}
";

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldChangePropertyTypeToFactory()
    {
        var test = EFCoreMock + @"
public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext { }

public class MyService
{
    public AppDbContext {|LC030:Db|} { get; set; }
}
";

        var fixedCode = EFCoreMock + @"
public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext { }

public class MyService
{
    public IDbContextFactory<AppDbContext> Db { get; set; }
}
";

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldNotDoubleAppendFactory()
    {
        var test = EFCoreMock + @"
public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext { }

public class MyService
{
    private readonly AppDbContext {|LC030:_dbFactory|};
}
";

        var fixedCode = EFCoreMock + @"
public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext { }

public class MyService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
}
";

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldHandleFullyQualifiedDbContextField()
    {
        var test = EFCoreMock + @"
public class MyService
{
    private readonly Microsoft.EntityFrameworkCore.DbContext {|LC030:_db|};
}
";

        var fixedCode = EFCoreMock + @"
public class MyService
{
    private readonly IDbContextFactory<Microsoft.EntityFrameworkCore.DbContext> _dbFactory;
}
";

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldRewriteMethodBodyToUseCreatedContext()
    {
        var test = EFCoreMock + @"
public class AppDbContext : DbContext
{
    public UserSet Users { get; } = new();
}

public class UserSet
{
    public int Count => 42;
}

public class MyService
{
    private readonly AppDbContext {|LC030:_db|};

    public MyService(AppDbContext db)
    {
        _db = db;
    }

    public int CountUsers()
    {
        return _db.Users.Count;
    }
}
";

        var fixedCode = EFCoreMock + @"
public class AppDbContext : DbContext
{
    public UserSet Users { get; } = new();
}

public class UserSet
{
    public int Count => 42;
}

public class MyService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public MyService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public int CountUsers()
    {
        using var db = _dbFactory.CreateDbContext();
        return db.Users.Count;
    }
}
";

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldRewriteExpressionBodiedMethodToUseCreatedContext()
    {
        var test = EFCoreMock + @"
public class AppDbContext : DbContext
{
    public UserSet Users { get; } = new();
}

public class UserSet
{
    public int Count => 42;
}

public class MyService
{
    private readonly AppDbContext {|LC030:_db|};

    public MyService(AppDbContext db) => _db = db;

    public int CountUsers() => _db.Users.Count;
}
";

        var fixedCode = EFCoreMock + @"
public class AppDbContext : DbContext
{
    public UserSet Users { get; } = new();
}

public class UserSet
{
    public int Count => 42;
}

public class MyService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public MyService(IDbContextFactory<AppDbContext> dbFactory) => _dbFactory = dbFactory;

    public int CountUsers()
    {
        using var db = _dbFactory.CreateDbContext();
        return db.Users.Count;
    }
}
";

        await VerifyFix(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldRewritePropertyUsagesToCreateContext()
    {
        var test = EFCoreMock + @"
public class AppDbContext : DbContext
{
    public UserSet Users { get; } = new();
}

public class UserSet
{
    public int Count => 42;
}

public class MyService
{
    public AppDbContext {|LC030:Db|} { get; set; } = null!;

    public int CountUsers()
    {
        return Db.Users.Count;
    }
}
";

        var fixedCode = EFCoreMock + @"
public class AppDbContext : DbContext
{
    public UserSet Users { get; } = new();
}

public class UserSet
{
    public int Count => 42;
}

public class MyService
{
    public IDbContextFactory<AppDbContext> Db { get; set; } = null!;

    public int CountUsers()
    {
        using var db = Db.CreateDbContext();
        return db.Users.Count;
    }
}
";

        await VerifyFix(test, fixedCode);
    }

    private static async Task VerifyFix(string test, string fixedCode)
    {
        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            CompilerDiagnostics = CompilerDiagnostics.None
        };

        await testObj.RunAsync();
    }
}
