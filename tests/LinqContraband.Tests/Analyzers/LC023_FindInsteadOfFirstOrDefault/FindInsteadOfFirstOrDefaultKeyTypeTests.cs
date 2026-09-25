using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultAnalyzer,
    LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault.FindInsteadOfFirstOrDefaultFixer>;

namespace LinqContraband.Tests.Analyzers.LC023_FindInsteadOfFirstOrDefault;

// Find throws ArgumentException when the key value's type differs from the key property's type, so the fix
// must pass a value of exactly the key type.
public partial class FindInsteadOfFirstOrDefaultTests
{
    private static string KeyTypeCode(string keyType, string method) => @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class Tag { public " + keyType + @" Id { get; set; } }

    public class TestClass
    {
" + method + @"
    }
}";

    [Fact]
    public async Task Fixer_CastsWideningKeyValueToKeyType()
    {
        var test = KeyTypeCode("long", @"        public Tag ById(DbSet<Tag> tags, int id) => {|LC023:tags.FirstOrDefault(t => t.Id == id)|};");
        var fixedCode = KeyTypeCode("long", @"        public Tag ById(DbSet<Tag> tags, int id) => tags.Find((long)id);");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_ParenthesizesCompoundKeyValueInCast()
    {
        var test = KeyTypeCode("long", @"        public Tag Next(DbSet<Tag> tags, int id) => {|LC023:tags.FirstOrDefault(t => id + 1 == t.Id)|};");
        var fixedCode = KeyTypeCode("long", @"        public Tag Next(DbSet<Tag> tags, int id) => tags.Find((long)(id + 1));");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_KeepsMatchingKeyValueUncast()
    {
        var test = KeyTypeCode("long", @"        public Tag ById(DbSet<Tag> tags, long id) => {|LC023:tags.FirstOrDefault(t => t.Id == id)|};");
        var fixedCode = KeyTypeCode("long", @"        public Tag ById(DbSet<Tag> tags, long id) => tags.Find(id);");

        await VerifyFix.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task Fixer_NullableKeyValue_ReportsWithoutFix()
    {
        // `t.Id == id` with an int? id is false for null; Find(id) would throw on a null or boxed int? key.
        var test = KeyTypeCode("int", @"        public Tag ById(DbSet<Tag> tags, int? id) => {|LC023:tags.FirstOrDefault(t => t.Id == id)|};");

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task Fixer_UserDefinedConversionKeyValue_ReportsWithoutFix()
    {
        var test = @"using Microsoft.EntityFrameworkCore;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public struct TagId
    {
        public int Value;
        public static implicit operator int(TagId id) => id.Value;
    }

    public class Tag { public int Id { get; set; } }

    public class TestClass
    {
        public Tag ById(DbSet<Tag> tags, TagId id) => {|LC023:tags.FirstOrDefault(t => t.Id == id)|};
    }
}";

        await VerifyFix.VerifyCodeFixAsync(test, test);
    }
}
