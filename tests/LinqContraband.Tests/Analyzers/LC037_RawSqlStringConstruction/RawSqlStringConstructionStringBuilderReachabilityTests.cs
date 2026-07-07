using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC037_RawSqlStringConstruction.RawSqlStringConstructionAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC037_RawSqlStringConstruction;

public partial class RawSqlStringConstructionTests
{
    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendInReturningGuard_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool skip)
        {
            var builder = new StringBuilder();
            if (skip)
            {
                builder.Append(""UPDATE Users SET Name = '"");
                builder.Append(id);
                return;
            }

            builder.Append(""UPDATE Users SET Active = 1"");
            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeNestedTerminatingGuard_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool skip, bool retry)
        {
            var builder = new StringBuilder();
            if (skip)
            {
                builder.Append(""UPDATE Users SET Name = '"");
                builder.Append(id);
                if (retry)
                {
                    return;
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }

            builder.Append(""UPDATE Users SET Active = 1"");
            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderClearInOnlyReachingBranch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
	using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool safe)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);
            if (safe)
            {
                builder.Clear();
            }
            else
            {
                return;
            }

            builder.Append(""UPDATE Users SET Active = 1"");
            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderCatchOnlyBranchClear_ShouldStillTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool safe)
        {
            var builder = new StringBuilder();
            builder.Append(""UPDATE Users SET Name = '"");
            builder.Append(id);

            try
            {
                MaybeThrow();
            }
            catch (InvalidOperationException)
            {
                if (safe)
                {
                    builder.Clear();
                }
                else
                {
                    return;
                }
            }

            builder.Append(""UPDATE Users SET Active = 1"");
            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }

        private static void MaybeThrow() { }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendInReturningLoop_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, bool retry)
        {
            var builder = new StringBuilder();
            while (retry)
            {
                builder.Append(""UPDATE Users SET Name = '"");
                builder.Append(id);
                return;
            }

            builder.Append(""UPDATE Users SET Active = 1"");
            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendInReturningSwitchSection_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id, int mode)
        {
            var builder = new StringBuilder();
            switch (mode)
            {
                case 1:
                    builder.Append(""UPDATE Users SET Name = '"");
                    builder.Append(id);
                    return;
            }

            builder.Append(""UPDATE Users SET Active = 1"");
            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeCaughtThrow_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
		using System;
		using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder();
            try
            {
                builder.Append(""UPDATE Users SET Name = '"");
                builder.Append(id);
                throw new InvalidOperationException();
            }
            catch (InvalidOperationException)
            {
            }

            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeThrowCaughtByBaseType_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
			using System;
			using System.Text;" + EfMock + @"
	namespace TestApp
	{
	    public sealed class Program
	    {
	        public void Run(DbContext db, int id)
	        {
	            var builder = new StringBuilder();
	            try
	            {
	                builder.Append(""UPDATE Users SET Name = '"");
	                builder.Append(id);
	                throw new InvalidOperationException();
	            }
	            catch (SystemException)
	            {
	            }

	            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
	        }
	    }
	}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeThrowCaughtByOrdinaryBaseType_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
				using System;
				using System.Text;" + EfMock + @"
		namespace TestApp
		{
		    public sealed class Program
		    {
		        public void Run(DbContext db, int id)
		        {
		            var builder = new StringBuilder();
		            try
		            {
		                builder.Append(""UPDATE Users SET Name = '"");
		                builder.Append(id);
		                throw new ArgumentNullException(nameof(id));
		            }
		            catch (ArgumentException)
		            {
		            }

		            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
		        }
		    }
		}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeThrowCaughtByAliasType_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
				using System;
				using System.Text;
                using Alias = System.InvalidOperationException;" + EfMock + @"
			namespace TestApp
			{
			    public sealed class Program
			    {
			        public void Run(DbContext db, int id)
			        {
			            var builder = new StringBuilder();
			            try
			            {
			                builder.Append(""UPDATE Users SET Name = '"");
			                builder.Append(id);
			                throw new InvalidOperationException();
			            }
			            catch (Alias)
			            {
			            }

			            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
			        }
			    }
			}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeCustomThrowCaughtByBaseType_ShouldTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
					using System;
					using System.Text;" + EfMock + @"
			namespace TestApp
			{
			    public sealed class Program
			    {
			        public void Run(DbContext db, int id)
			        {
			            var builder = new StringBuilder();
			            try
			            {
			                builder.Append(""UPDATE Users SET Name = '"");
			                builder.Append(id);
			                throw new MyException();
			            }
			            catch (InvalidOperationException)
			            {
			            }

			            var result = db.Database.ExecuteSqlRaw({|LC037:builder.ToString()|});
			        }

                    private sealed class MyException : InvalidOperationException
                    {
                    }
			    }
			}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeCustomThrowNameSuffixNotCaughtByBaseType_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
						using System;
						using System.Text;" + EfMock + @"
				namespace TestApp
				{
				    public sealed class Program
				    {
				        public void Run(DbContext db, int id)
				        {
				            var builder = new StringBuilder();
				            try
				            {
				                builder.Append(""UPDATE Users SET Name = '"");
				                builder.Append(id);
				                throw new MyInvalidOperationException();
				            }
				            catch (InvalidOperationException)
				            {
				            }

				            var result = db.Database.ExecuteSqlRaw(builder.ToString());
				        }

	                    private sealed class MyInvalidOperationException : Exception
	                    {
	                    }
				    }
				}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeThrowHandledByReturningFirstCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
						using System;
						using System.Text;" + EfMock + @"
					namespace TestApp
					{
					    public sealed class Program
					    {
					        public void Run(DbContext db, int id)
					        {
					            var builder = new StringBuilder();
					            try
					            {
					                builder.Append(""UPDATE Users SET Name = '"");
					                builder.Append(id);
					                throw new InvalidOperationException();
					            }
					            catch (InvalidOperationException)
					            {
					                return;
					            }
					            catch (Exception)
					            {
					            }

					            var result = db.Database.ExecuteSqlRaw(builder.ToString());
					        }
					    }
					}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeThrowNotCaughtByApplicationException_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
						using System;
						using System.Text;" + EfMock + @"
					namespace TestApp
					{
					    public sealed class Program
					    {
					        public void Run(DbContext db, int id)
					        {
					            var builder = new StringBuilder();
					            try
					            {
					                builder.Append(""UPDATE Users SET Name = '"");
					                builder.Append(id);
					                throw new InvalidOperationException();
					            }
					            catch (ApplicationException)
					            {
					            }

					            var result = db.Database.ExecuteSqlRaw(builder.ToString());
					        }
					    }
					}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeThrowCaughtByFalseFilter_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
						using System;
						using System.Text;" + EfMock + @"
					namespace TestApp
					{
					    public sealed class Program
					    {
					        public void Run(DbContext db, int id)
					        {
					            var builder = new StringBuilder();
					            try
					            {
					                builder.Append(""UPDATE Users SET Name = '"");
					                builder.Append(id);
					                throw new InvalidOperationException();
					            }
					            catch (InvalidOperationException) when (false)
					            {
					            }

					            var result = db.Database.ExecuteSqlRaw(builder.ToString());
					        }
					    }
					}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendBeforeCaughtThrowAndReturningCatch_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
					using System;
	using System.Text;" + EfMock + @"
	namespace TestApp
	{
	    public sealed class Program
	    {
	        public void Run(DbContext db, int id)
	        {
	            var builder = new StringBuilder();
	            try
	            {
	                builder.Append(""UPDATE Users SET Name = '"");
	                builder.Append(id);
	                throw new InvalidOperationException();
	            }
	            catch (InvalidOperationException)
	            {
	                return;
	            }

	            var result = db.Database.ExecuteSqlRaw(builder.ToString());
	        }
	    }
	}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderAppendLocalOverwrittenByOnlyReachingConstant_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
		using System.Text;" + EfMock + @"
	namespace TestApp
	{
	    public sealed class Program
	    {
	        public void Run(DbContext db, int id, bool safe)
	        {
	            var builder = new StringBuilder();
	            var predicate = id.ToString();
	            if (safe)
	            {
	                predicate = ""Active = 1"";
	            }
	            else
	            {
	                return;
	            }

	            builder.Append(""UPDATE Users SET "");
	            builder.Append(predicate);
	            var result = db.Database.ExecuteSqlRaw(builder.ToString());
	        }
	    }
	}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithStringBuilderConstructorTaintClearedInFluentChain_ShouldNotTrigger()
    {
        var test = @"using Microsoft.EntityFrameworkCore;
		using System.Text;" + EfMock + @"
namespace TestApp
{
    public sealed class Program
    {
        public void Run(DbContext db, int id)
        {
            var builder = new StringBuilder(id.ToString())
                .Clear()
                .Append(""UPDATE Users SET Active = 1"");

            var result = db.Database.ExecuteSqlRaw(builder.ToString());
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

}
