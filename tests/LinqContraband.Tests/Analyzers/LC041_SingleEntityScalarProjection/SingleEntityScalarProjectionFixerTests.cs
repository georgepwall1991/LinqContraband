using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC041_SingleEntityScalarProjection.SingleEntityScalarProjectionAnalyzer,
    LinqContraband.Analyzers.LC041_SingleEntityScalarProjection.SingleEntityScalarProjectionFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC041_SingleEntityScalarProjection.SingleEntityScalarProjectionAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC041_SingleEntityScalarProjection.SingleEntityScalarProjectionAnalyzer,
    LinqContraband.Analyzers.LC041_SingleEntityScalarProjection.SingleEntityScalarProjectionFixer>;

namespace LinqContraband.Tests.Analyzers.LC041_SingleEntityScalarProjection;

public partial class SingleEntityScalarProjectionTests
{
    [Fact]
    public async Task Fixer_ShouldProjectSingleConsumedProperty()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public void Run(DbSet<User> users)
        {
            var user = {|LC041:users.First(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public void Run(DbSet<User> users)
        {
            var user = users.Where(x => x.IsActive).Select(x => x.Name).First();
            System.Console.WriteLine(user);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ShouldProjectSingleConsumedProperty_AfterWhereStep()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public void Run(DbSet<User> users)
        {
            var user = {|LC041:users.Where(x => x.IsActive).First()|};
            Use(user.Name);
        }

        private static void Use(string value) { }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public void Run(DbSet<User> users)
        {
            var user = users.Where(x => x.IsActive).Select(x => x.Name).First();
            Use(user);
        }

        private static void Use(string value) { }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task First_WithHoistedPredicate_ShouldNotOfferFix()
    {
        var test = @"using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public void Run(DbSet<User> users, Expression<Func<User, bool>> active)
        {
            var user = {|LC041:users.First(active)|};
            Use(user.Name);
        }

        private static void Use(string value) { }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_ShouldProjectSingleConsumedProperty_OnAsyncCall()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public async Task Run(DbSet<User> users)
        {
            var user = await {|LC041:users.SingleAsync(x => x.IsActive)|};
            System.Console.WriteLine(user.Name.Length);
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public async Task Run(DbSet<User> users)
        {
            var user = await users.Where(x => x.IsActive).Select(x => x.Name).SingleAsync();
            System.Console.WriteLine(user.Length);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task FirstOrDefault_ShouldNotOfferFix()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public void Run(DbSet<User> users)
        {
            var user = {|LC041:users.FirstOrDefault(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task SingleOrDefaultAsync_ShouldNotOfferFix()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public async Task Run(DbSet<User> users)
        {
            var user = await {|LC041:users.SingleOrDefaultAsync(x => x.IsActive)|};
            System.Console.WriteLine(user.Name.Length);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task ExplicitTypeDeclaration_ShouldNotOfferFix()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public void Run(DbSet<User> users)
        {
            User user = {|LC041:users.FirstOrDefault(x => x.IsActive)|};
            System.Console.WriteLine(user.Name);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FixAll_RewritesMultipleScalarProjections()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public void Run(DbSet<User> users)
        {
            var activeUser = {|#0:users.First(x => x.IsActive)|};
            System.Console.WriteLine(activeUser.Name);

            var inactiveUser = {|#1:users.Single(x => !x.IsActive)|};
            System.Console.WriteLine(inactiveUser.Name);
        }
    }
}";

        var fixedCode = @"using Microsoft.EntityFrameworkCore;" + EfCoreMock + @"
namespace TestApp
{
    public class User
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }
    }

    public class TestClass
    {
        public void Run(DbSet<User> users)
        {
            var activeUser = users.Where(x => x.IsActive).Select(x => x.Name).First();
            System.Console.WriteLine(activeUser);

            var inactiveUser = users.Where(x => !x.IsActive).Select(x => x.Name).Single();
            System.Console.WriteLine(inactiveUser);
        }
    }
}";

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixEquivalenceKey = "ProjectConsumedScalar"
        };

        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC041")
                .WithArguments("First", "Name")
                .WithLocation(0));
        testObj.ExpectedDiagnostics.Add(
            VerifyFix.Diagnostic("LC041")
                .WithArguments("Single", "Name")
                .WithLocation(1));

        await testObj.RunAsync();
    }
}
