using System.Threading.Tasks;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<LinqContraband.AnyOverCountAnalyzer>;

namespace LinqContraband.Tests
{
    public class AnyOverCountTests
    {
        private const string Usings = @"
using System;
using System.Collections.Generic;
using System.Linq;
";
        private const string MockNamespace = @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            // Placeholder
        }
    }
}";

        [Fact]
        public async Task CountGreaterThanZero_OnIQueryable_ShouldTriggerLC003()
        {
            var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:query.Count() > 0|})
            {
            }
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Fact]
        public async Task CountWithPredicateGreaterThanZero_OnIQueryable_ShouldTriggerLC003()
        {
            var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:query.Count(x => x > 10) > 0|})
            {
            }
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Fact]
        public async Task LongCountGreaterThanZero_OnIQueryable_ShouldTriggerLC003()
        {
            var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:query.LongCount() > 0|})
            {
            }
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
        
        [Fact]
        public async Task ZeroLessThanCount_OnIQueryable_ShouldTriggerLC003()
        {
             var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if ({|LC003:0 < query.Count()|})
            {
            }
        }
    }
}";
             await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Fact]
        public async Task CountGreaterThanZero_OnList_ShouldNotTrigger()
        {
            var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var list = new List<int>();
            if (list.Count > 0)
            {
            }
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
        
        [Fact]
        public async Task CountEqualsZero_ShouldNotTrigger_ForNow()
        {
            // Analyzer is specifically for Any() replacement which implies > 0. 
            // == 0 would be !Any(), which is a valid extension but let's stick to the requirement first.
            var test = Usings + @"
namespace LinqContraband.Test
{
    public class TestClass
    {
        public void TestMethod()
        {
            var query = new List<int>().AsQueryable();
            if (query.Count() == 0)
            {
            }
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}

