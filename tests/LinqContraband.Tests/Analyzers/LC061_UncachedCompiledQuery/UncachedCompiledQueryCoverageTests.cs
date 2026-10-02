using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC061_UncachedCompiledQuery.UncachedCompiledQueryAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC061_UncachedCompiledQuery;

/// <summary>
/// Leftover 5.16.0 LC061 arms the original 112 analyzer cases do not isolate.
/// <c>ClimbValue</c> walks casts, <c>!</c>, <c>?:</c> and <c>??</c> before judging invocation;
/// the shipped fixtures only wrap parentheses. <c>PatternTestsNull</c> treats <c>{ }</c> as
/// not-null; <c>IsNull</c> treats the <c>default</c> literal like <c>null</c>.
/// A parenthesized <c>is (null)</c> pattern is not pinned (it does not stay quiet).
/// <c>IMemoryCache.GetOrCreate</c>/<c>GetOrCreateAsync</c>,
/// <c>HybridCache.GetOrCreateAsync</c> and <c>ImmutableInterlocked.GetOrAdd</c> are named in
/// <c>CacheFactoryMethods</c> and stay quiet only when the cache is kept outside the call.
/// </summary>
public class UncachedCompiledQueryCoverageTests
{
    /// <summary>
    /// Minimal shims whose metadata names match <c>CacheFactoryMethods</c>. The verifier
    /// compilation does not reference the real Memory/Hybrid packages.
    /// </summary>
    internal const string CacheMocks = @"
namespace Microsoft.Extensions.Caching.Memory
{
    public interface IMemoryCache { }
    public interface ICacheEntry { }

    public static class CacheExtensions
    {
        public static TItem GetOrCreate<TItem>(this IMemoryCache cache, object key, System.Func<ICacheEntry, TItem> factory) => factory(null);
        public static TItem GetOrCreateAsync<TItem>(this IMemoryCache cache, object key, System.Func<ICacheEntry, TItem> factory) => factory(null);
    }
}

namespace Microsoft.Extensions.Caching.Hybrid
{
    public class HybridCache
    {
        public T GetOrCreateAsync<T>(string key, System.Func<string, T> factory) => factory(key);
    }
}

namespace System.Collections.Immutable
{
    public static class ImmutableInterlocked
    {
        public static TValue GetOrAdd<TKey, TValue>(ref TValue location, TKey key, System.Func<TKey, TValue> valueFactory) => valueFactory(key);
    }
}
";

    private static string Wrap(string body) => UncachedCompiledQueryTests.Wrap(body);

    private static string WrapMembers(string members) => UncachedCompiledQueryTests.WrapMembers(members);

    private static string WrapMembersWithCaches(string members)
    {
        return WrapMembers(members)
            .Replace(
                "using Microsoft.EntityFrameworkCore;",
                "using Microsoft.EntityFrameworkCore;\nusing Microsoft.Extensions.Caching.Memory;\nusing Microsoft.Extensions.Caching.Hybrid;\nusing System.Collections.Immutable;")
            + CacheMocks;
    }

    [Fact]
    public async Task Cast_ThenInvoke_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(
            Wrap(@"return ((Func<Ctx, int, Blog>){|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)))(_db, id);"),
            VerifyCS.Diagnostic().WithLocation(0).WithArguments("CompileQuery"));
    }

    [Fact]
    public async Task SuppressNullable_ThenInvoke_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(
            Wrap(@"return ({|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i))!)(_db, id);"),
            VerifyCS.Diagnostic().WithLocation(0).WithArguments("CompileQuery"));
    }

    [Fact]
    public async Task Ternary_ThenInvoke_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(
            Wrap(@"Func<Ctx, int, Blog> fallback = (c, i) => null; return (id > 0 ? {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)) : fallback)(_db, id);"),
            VerifyCS.Diagnostic().WithLocation(0).WithArguments("CompileQuery"));
    }

    [Fact]
    public async Task Coalesce_ThenInvoke_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(
            Wrap(@"return ({|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)) ?? ((Ctx c, int i) => null))(_db, id);"),
            VerifyCS.Diagnostic().WithLocation(0).WithArguments("CompileQuery"));
    }

    [Fact]
    public async Task EmptyPropertyPattern_TrueBranch_Reports()
    {
        // `{ }` means not-null. Storing in the true branch is not a lazy cache.
        await VerifyCS.VerifyAnalyzerAsync(
            WrapMembers(@"
    private Func<Ctx, int, Blog> _byId;
    public Blog Get(int id) { if (_byId is { }) _byId = {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return _byId(_db, id); }"),
            VerifyCS.Diagnostic().WithLocation(0).WithArguments("CompileQuery"));
    }

    [Fact]
    public async Task EmptyPropertyPattern_ElseBranch_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapMembers(@"
    private Func<Ctx, int, Blog> _byId;
    public Blog Get(int id) { if (_byId is { }) { } else { _byId = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); } return _byId(_db, id); }"));
    }

    [Fact]
    public async Task EqualsDefaultLiteral_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapMembers(@"
    private Func<Ctx, int, Blog> _byId;
    public Blog Get(int id) { if (_byId == default) _byId = EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)); return _byId(_db, id); }"));
    }

    [Fact]
    public async Task IMemoryCache_GetOrCreate_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapMembersWithCaches(@"
    private readonly IMemoryCache _cache;
    public Blog Get(string key, int id) => _cache.GetOrCreate(key, _ => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)))(_db, id);"));
    }

    [Fact]
    public async Task IMemoryCache_GetOrCreateAsync_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapMembersWithCaches(@"
    public Blog Get(IMemoryCache cache, string key, int id) => cache.GetOrCreateAsync(key, _ => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)))(_db, id);"));
    }

    [Fact]
    public async Task HybridCache_GetOrCreateAsync_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapMembersWithCaches(@"
    private readonly HybridCache _cache;
    public Blog Get(string key, int id) => _cache.GetOrCreateAsync(key, _ => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)))(_db, id);"));
    }

    [Fact]
    public async Task ImmutableInterlocked_GetOrAdd_StaysQuiet()
    {
        await VerifyCS.VerifyAnalyzerAsync(WrapMembersWithCaches(@"
    private static Func<Ctx, int, Blog> _byId;
    public Blog Get(int id) => ImmutableInterlocked.GetOrAdd(ref _byId, 0, _ => EF.CompileQuery((Ctx c, int i) => c.Blogs.First(b => b.Id == i)))(_db, id);"));
    }

    [Fact]
    public async Task IMemoryCache_LocalCopy_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(
            WrapMembersWithCaches(@"
    private readonly IMemoryCache _cache;
    public Blog Get(string key, int id) { var cache = _cache; return cache.GetOrCreate(key, _ => {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)))(_db, id); }"),
            VerifyCS.Diagnostic().WithLocation(0).WithArguments("CompileQuery"));
    }

    [Fact]
    public async Task ImmutableInterlocked_LocalRef_Reports()
    {
        await VerifyCS.VerifyAnalyzerAsync(
            WrapMembersWithCaches(@"
    public Blog Get(int id) { Func<Ctx, int, Blog> q = null; return ImmutableInterlocked.GetOrAdd(ref q, 0, _ => {|#0:EF.CompileQuery|}((Ctx c, int i) => c.Blogs.First(b => b.Id == i)))(_db, id); }"),
            VerifyCS.Diagnostic().WithLocation(0).WithArguments("CompileQuery"));
    }
}
