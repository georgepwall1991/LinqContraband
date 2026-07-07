using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC041_SingleEntityScalarProjection.SingleEntityScalarProjectionAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC041_SingleEntityScalarProjection.SingleEntityScalarProjectionAnalyzer,
    LinqContraband.Analyzers.LC041_SingleEntityScalarProjection.SingleEntityScalarProjectionFixer>;

namespace LinqContraband.Tests.Analyzers.LC041_SingleEntityScalarProjection;

public partial class SingleEntityScalarProjectionTests
{
    [Fact]
    public async Task FirstOrDefault_WithNullConditionalSinglePropertyUsage_Triggers()
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
            System.Console.WriteLine(user?.Name);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithNullConditionalSinglePropertyChainUsage_Triggers()
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
            System.Console.WriteLine(user?.Name.Length);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithNullConditionalSinglePropertyMethodUsage_Triggers()
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
            System.Console.WriteLine(user?.Name.Trim());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_WithNullConditionalSinglePropertyUsage_ShouldNotOfferFix()
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
            System.Console.WriteLine(user?.Name);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task First_WithNullConditionalSinglePropertyUsage_ShouldNotOfferFix()
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
            System.Console.WriteLine(user?.Name);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task First_WithNullConditionalSinglePropertyChainUsage_ShouldNotOfferFix()
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
            System.Console.WriteLine(user?.Name.Length);
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task First_WithNullConditionalSinglePropertyMethodUsage_ShouldNotOfferFix()
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
            System.Console.WriteLine(user?.Name.Trim());
        }
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }
}
