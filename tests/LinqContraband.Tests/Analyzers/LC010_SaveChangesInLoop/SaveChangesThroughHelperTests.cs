using Microsoft.CodeAnalysis.Testing;
using LinqContraband.Analyzers.LC010_SaveChangesInLoop;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopAnalyzer,
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopFixer>;

namespace LinqContraband.Tests.Analyzers.LC010_SaveChangesInLoop;

/// <summary>
/// A repository whose <c>Add</c> saves, or a unit of work whose <c>Commit</c> is called per item, hides the save
/// inside a helper. LC010 reads same-project helpers up to three calls deep, and interface or virtual calls when
/// every implementation saves, and reports the call in the loop. It stays quiet when the helper saves a context it
/// created itself, when the object or context is created per iteration, and on batch loops.
/// </summary>
public class SaveChangesThroughHelperTests
{
    private const string Mock = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
        public void Add(object entity) { }
        public void Dispose() {}
    }
}

public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext
{
}

public interface IUnitOfWork
{
    void Commit();
}

public class UnitOfWork : IUnitOfWork
{
    private readonly MyDbContext _db;
    public UnitOfWork(MyDbContext db) => _db = db;
    public void Commit() => _db.SaveChanges();
}

public interface IRepository<T>
{
    void Add(T item);
    void Track(T item);
}

public class Repository<T> : IRepository<T>
{
    private readonly MyDbContext _db;
    public Repository(MyDbContext db) => _db = db;

    public virtual void Add(T item)
    {
        _db.Add(item);
        _db.SaveChanges();
    }

    public void Track(T item) => _db.Add(item);
}
";

    private static string Program(string members) => @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

class Program
{
    private readonly MyDbContext _db = new MyDbContext();
    private readonly IUnitOfWork _unitOfWork = null;
    private readonly IRepository<string> _repository = null;
    private readonly Repository<string> _concrete = null;
" + members + @"
}
" + Mock;

    private static DiagnosticResult Helper(int location, string helper, string save) =>
        VerifyCS.Diagnostic(SaveChangesInLoopAnalyzer.HelperCallRule).WithLocation(location).WithArguments(helper, save);

    [Fact]
    public Task HelperThatSaves_CalledInLoop_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            Program(@"
    async Task Run(List<string> items)
    {
        foreach (var item in items)
        {
            {|#0:Store(item)|};
            await {|#1:StoreAsync(item)|};
            {|#2:Outer(item)|};
        }
    }

    private void Store(string item)
    {
        _db.Add(item);
        _db.SaveChanges();
    }

    private async Task StoreAsync(string item)
    {
        _db.Add(item);
        await _db.SaveChangesAsync();
    }

    private void Outer(string item) => Store(item);"),
            Helper(0, "Store", "SaveChanges"),
            Helper(1, "StoreAsync", "SaveChangesAsync"),
            Helper(2, "Outer", "SaveChanges"));

    [Fact]
    public Task HelperThatSaves_ThroughInterfaceVirtualAndLambda_Reports() =>
        VerifyCS.VerifyAnalyzerAsync(
            Program(@"
    void Run(List<string> items)
    {
        foreach (var item in items)
        {
            {|#0:_unitOfWork.Commit()|};
            {|#1:_repository.Add(item)|};
            {|#2:_concrete.Add(item)|};
        }

        items.ForEach(item => {|#3:_repository.Add(item)|});
    }"),
            Helper(0, "Commit", "SaveChanges"),
            Helper(1, "Add", "SaveChanges"),
            Helper(2, "Add", "SaveChanges"),
            Helper(3, "Add", "SaveChanges"));

    [Fact]
    public Task HelperThatSaves_ItsOwnOrAPerIterationContext_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            Program(@"
    void Run(List<string> items)
    {
        foreach (var item in items)
        {
            StoreInOwnContext(item);
            _repository.Track(item);

            using var fresh = new MyDbContext();
            StoreIn(fresh, item);

            var repository = new Repository<string>(fresh);
            repository.Add(item);
        }

        _unitOfWork.Commit();
        items.ForEach(item => new Repository<string>(new MyDbContext()).Track(item));
    }

    private void StoreInOwnContext(string item)
    {
        using var db = new MyDbContext();
        db.Add(item);
        db.SaveChanges();
    }

    private static void StoreIn(MyDbContext db, string item)
    {
        db.Add(item);
        db.SaveChanges();
    }"));

    [Fact]
    public Task HelperThatSaves_WhenAnImplementationDoesNotSave_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            Program(@"
    void Run(List<string> items, ISink sink)
    {
        foreach (var item in items)
            sink.Write(item);
    }
}

public interface ISink
{
    void Write(string item);
}

public class DbSink : ISink
{
    private readonly MyDbContext _db = new MyDbContext();
    public void Write(string item)
    {
        _db.Add(item);
        _db.SaveChanges();
    }
}

public class BufferedSink : ISink
{
    private readonly List<string> _buffer = new List<string>();
    public void Write(string item) => _buffer.Add(item);"));

    [Fact]
    public Task HelperThatSaves_InBatchOrSinglePassLoop_IsQuiet() =>
        VerifyCS.VerifyAnalyzerAsync(
            Program(@"
    void Run(List<string> items)
    {
        foreach (var item in items)
        {
            Store(item);
            return;
        }
    }

    private void Store(string item)
    {
        _db.Add(item);
        _db.SaveChanges();
    }"));

    [Fact]
    public async Task HelperThatSaves_HasNoCodeFix()
    {
        var test = Program(@"
    void Run(List<string> items)
    {
        var index = 0;
        do
        {
            _db.Add(items[index++]);
            {|#0:Commit()|};
        } while (index < items.Count);
    }

    private void Commit() => _db.SaveChanges();");

        await VerifyFix.VerifyCodeFixAsync(test, Helper(0, "Commit", "SaveChanges"), test);
    }
}
