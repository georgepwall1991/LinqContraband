using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey.EntityMissingPrimaryKeyAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC011_EntityMissingPrimaryKey;

public partial class EntityMissingPrimaryKeyTests
{
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
    public async Task TestCrime_PrivateIdProperty_ShouldTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<PrivateKeyEntity> PrivateKeys { get; set; }
    }

    public class PrivateKeyEntity
    {
        private int Id { get; set; }
        public string Name { get; set; }
    }
}";

        var expected = VerifyCS.Diagnostic("LC011")
            .WithSpan(48, 40, 48, 51)
            .WithArguments("PrivateKeyEntity");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestCrime_NavigationPropertyNamedId_ShouldTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<NavIdEntity> NavIdEntities { get; set; }
    }

    public class OtherEntity
    {
        public int Id { get; set; }
    }

    public class NavIdEntity
    {
        public OtherEntity Id { get; set; }  // Navigation property named Id - not a valid key
        public string Name { get; set; }
    }
}";

        var expected = VerifyCS.Diagnostic("LC011")
            .WithSpan(48, 35, 48, 48)
            .WithArguments("NavIdEntity");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TestInnocent_GuidIdProperty_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<GuidEntity> GuidEntities { get; set; }
    }

    public class GuidEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_NullableIntIdProperty_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<NullableIdEntity> NullableIdEntities { get; set; }
    }

    public class NullableIdEntity
    {
        public int? Id { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_LongIdProperty_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<LongIdEntity> LongIdEntities { get; set; }
    }

    public class LongIdEntity
    {
        public long Id { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TestInnocent_StringIdProperty_ShouldNotTrigger()
    {
        var test = Usings + MockAttributes + @"
        public DbSet<StringIdEntity> StringIdEntities { get; set; }
    }

    public class StringIdEntity
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
