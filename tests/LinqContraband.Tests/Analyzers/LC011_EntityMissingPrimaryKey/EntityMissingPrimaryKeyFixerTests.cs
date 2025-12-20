using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyAnalyzer,
    LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyFixer>;

namespace LinqContraband.Tests.Analyzers.LC011_EntityMissingPrimaryKey;

public class EntityMissingPrimaryKeyFixerTests
{
    [Fact]
    public async Task Fixer_ShouldAddIdProperty()
    {
        var test = @"
using Microsoft.EntityFrameworkCore;
namespace LinqContraband.Test
{
    public class AppDbContext : DbContext
    {
        public DbSet<Product> {|LC011:Products|} { get; set; }
    }
    public class Product
    {
        public string Name { get; set; }
    }
}
namespace Microsoft.EntityFrameworkCore { public class DbContext { } public class DbSet<T> { } }
";

        var fixedCode = @"
using Microsoft.EntityFrameworkCore;
namespace LinqContraband.Test
{
    public class AppDbContext : DbContext
    {
        public DbSet<Product> Products { get; set; }
    }
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
namespace Microsoft.EntityFrameworkCore { public class DbContext { } public class DbSet<T> { } }
";

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }
}
