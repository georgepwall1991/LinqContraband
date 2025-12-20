using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC022_ExplicitLoadingInLoop.ExplicitLoadingInLoopAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC022_ExplicitLoadingInLoop;

public class ExplicitLoadingInLoopTests
{
    private const string EFCoreMock = @"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore.ChangeTracking
{
    public class EntityEntry { 
        public ReferenceEntry Reference(string name) => null;
        public CollectionEntry Collection(string name) => null;
    }
    public class ReferenceEntry { 
        public void Load() { } 
        public Task LoadAsync() => Task.CompletedTask;
    }
    public class CollectionEntry { 
        public void Load() { } 
        public Task LoadAsync() => Task.CompletedTask;
    }
}
";

    [Fact]
    public async Task Load_InsideForEach_ShouldTriggerLC022()
    {
        var test = @"using Microsoft.EntityFrameworkCore.ChangeTracking;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(IEnumerable<ReferenceEntry> entries)
        {
            foreach (var entry in entries)
            {
                {|LC022:entry.Load()|};
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LoadAsync_InsideWhile_ShouldTriggerLC022()
    {
        var test = @"using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Threading.Tasks;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public async Task TestMethod(ReferenceEntry entry)
        {
            while (true)
            {
                await {|LC022:entry.LoadAsync()|};
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Load_OutsideLoop_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore.ChangeTracking;" + EFCoreMock + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod(ReferenceEntry entry)
        {
            entry.Load();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
