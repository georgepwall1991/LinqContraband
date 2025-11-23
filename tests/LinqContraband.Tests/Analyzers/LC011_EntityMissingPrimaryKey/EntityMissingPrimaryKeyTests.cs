using System.Threading.Tasks;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC011_EntityMissingPrimaryKey;

public class EntityMissingPrimaryKeyTests
{
    private const string Usings = @"
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TestNamespace;
";

    private const string MockAttributes = @"
namespace System.ComponentModel.DataAnnotations
{
    public class KeyAttribute : Attribute {}
}
namespace Microsoft.EntityFrameworkCore
{
    public class KeylessAttribute : Attribute {}
    public class PrimaryKeyAttribute : Attribute 
    {
        public PrimaryKeyAttribute(params string[] propertyNames) {}
    }
    public class DbContext : IDisposable
    {
        public void Dispose() {}
        protected virtual void OnModelCreating(ModelBuilder modelBuilder) {}
    }
    public class DbSet<T> where T : class {}
    
    public class ModelBuilder 
    {
        public EntityTypeBuilder<T> Entity<T>() where T : class => new EntityTypeBuilder<T>();
    }

    public class EntityTypeBuilder<T> where T : class
    {
        public EntityTypeBuilder<T> HasKey(params string[] propertyNames) => this;
        public EntityTypeBuilder<T> HasKey(System.Linq.Expressions.Expression<Func<T, object>> keyExpression) => this;
    }
}

namespace TestNamespace
{
    public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
";

    private const string MockEnd = @"
    }
}";

    [Fact]
    public async Task TestCrime_EntityNoKey_ShouldTriggerLC011()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<NoKeyEntity> NoKeys { get; set; }
    }

    public class NoKeyEntity
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }
}";

        var expected = VerifyCS.Diagnostic("LC011")
            .WithSpan(43, 35, 43, 41) // Adjust based on exact line count
            .WithArguments("NoKeyEntity");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_EntityWithId_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<ValidEntity> ValidEntities { get; set; }
    }

    public class ValidEntity
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_EntityWithTypeNameId_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<User> Users { get; set; }
    }

    public class User
    {
        public int UserId { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_EntityWithKeyAttribute_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<KeyEntity> KeyEntities { get; set; }
    }

    public class KeyEntity
    {
        [Key]
        public int CustomKey { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_EntityWithPrimaryKeyAttribute_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<PkEntity> PkEntities { get; set; }
    }

    [PrimaryKey(nameof(Name))]
    public class PkEntity
    {
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_KeylessEntity_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<KeylessView> Views { get; set; }
    }

    [Keyless]
    public class KeylessView
    {
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_InheritedKey_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<ChildEntity> Children { get; set; }
    }

    public class BaseEntity
    {
        public int Id { get; set; }
    }

    public class ChildEntity : BaseEntity
    {
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_FluentApiHasKey_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<FluentEntity> FluentEntities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FluentEntity>().HasKey(e => e.CustomId);
        }
    }

    public class FluentEntity
    {
        public int CustomId { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}

