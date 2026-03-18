using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches.UseFrozenSetForStaticMembershipCachesAnalyzer,
    LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches.UseFrozenSetForStaticMembershipCachesFixer,
    Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches.UseFrozenSetForStaticMembershipCachesAnalyzer,
    LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches.UseFrozenSetForStaticMembershipCachesFixer>;

namespace LinqContraband.Tests.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches;

public class UseFrozenSetForStaticMembershipCachesFixerTests
{
    private const string Usings = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
";

    private const string FrozenSupport = @"
namespace System.Collections.Frozen
{
    using System.Collections.Generic;

    public abstract class FrozenSet<T> : IEnumerable<T>
    {
        public abstract bool Contains(T item);
        public abstract IEnumerator<T> GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class FrozenSetExtensions
    {
        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source) => null;
        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source, IEqualityComparer<T> comparer) => null;
    }
}
";

    [Fact]
    public async Task CollectionInitializer_WithComparer_RewritesToFrozenSet()
    {
        var test = Usings + FrozenSupport + @"
class Program
{
    {|#0:private static readonly HashSet<string> ElevatedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ""admin"",
        ""ops""
    };|}

    static bool IsElevated(string role) => ElevatedRoles.Contains(role);
}";

        var fixedCode = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Frozen;

namespace System.Collections.Frozen
{
    using System.Collections.Generic;

    public abstract class FrozenSet<T> : IEnumerable<T>
    {
        public abstract bool Contains(T item);
        public abstract IEnumerator<T> GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class FrozenSetExtensions
    {
        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source) => null;
        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source, IEqualityComparer<T> comparer) => null;
    }
}

class Program
{
    private static readonly FrozenSet<string> ElevatedRoles = new string[] {
        ""admin"",
        ""ops""
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    static bool IsElevated(string role) => ElevatedRoles.Contains(role);
}";

        var expected = VerifyFix.Diagnostic("LC033").WithLocation(0).WithArguments("ElevatedRoles");
        await VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task SourceConstructor_RewritesToFrozenSet()
    {
        var test = Usings + FrozenSupport + @"
class Program
{
    private static readonly int[] SeedValues = { 1, 2, 3 };
    {|#0:private static readonly HashSet<int> ReservedIds = new HashSet<int>(SeedValues);|}

    static bool IsReserved(int value) => ReservedIds.Contains(value);
}";

        var fixedCode = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Frozen;

namespace System.Collections.Frozen
{
    using System.Collections.Generic;

    public abstract class FrozenSet<T> : IEnumerable<T>
    {
        public abstract bool Contains(T item);
        public abstract IEnumerator<T> GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class FrozenSetExtensions
    {
        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source) => null;
        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source, IEqualityComparer<T> comparer) => null;
    }
}

class Program
{
    private static readonly int[] SeedValues = { 1, 2, 3 };
    private static readonly FrozenSet<int> ReservedIds = SeedValues.ToFrozenSet();

    static bool IsReserved(int value) => ReservedIds.Contains(value);
}";

        var expected = VerifyFix.Diagnostic("LC033").WithLocation(0).WithArguments("ReservedIds");
        await VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task ToHashSetInitializer_RewritesToFrozenSetWithoutDuplicateUsing()
    {
        var test = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Frozen;
" + FrozenSupport + @"
class Program
{
    private static readonly string[] SeedValues = { ""admin"", ""ops"" };
    {|#0:private static readonly HashSet<string> ElevatedRoles = SeedValues.ToHashSet(StringComparer.OrdinalIgnoreCase);|}

    static bool IsElevated(string role) => ElevatedRoles.Contains(role);
}";

        var fixedCode = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Frozen;

namespace System.Collections.Frozen
{
    using System.Collections.Generic;

    public abstract class FrozenSet<T> : IEnumerable<T>
    {
        public abstract bool Contains(T item);
        public abstract IEnumerator<T> GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class FrozenSetExtensions
    {
        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source) => null;
        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source, IEqualityComparer<T> comparer) => null;
    }
}

class Program
{
    private static readonly string[] SeedValues = { ""admin"", ""ops"" };
    private static readonly FrozenSet<string> ElevatedRoles = SeedValues.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    static bool IsElevated(string role) => ElevatedRoles.Contains(role);
}";

        var expected = VerifyFix.Diagnostic("LC033").WithLocation(0).WithArguments("ElevatedRoles");
        await VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task FixAll_RewritesMultipleEligibleFields()
    {
        var test = Usings + FrozenSupport + @"
class Program
{
    private static readonly int[] SeedValues = { 1, 2, 3 };
    {|#0:private static readonly HashSet<int> ReservedIds = new HashSet<int>(SeedValues);|}
    {|#1:private static readonly HashSet<string> ElevatedRoles = new(StringComparer.OrdinalIgnoreCase) { ""admin"", ""ops"" };|}

    static bool Matches(int value, string role) => ReservedIds.Contains(value) && ElevatedRoles.Contains(role);
}";

        var fixedCode = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Frozen;

namespace System.Collections.Frozen
{
    using System.Collections.Generic;

    public abstract class FrozenSet<T> : IEnumerable<T>
    {
        public abstract bool Contains(T item);
        public abstract IEnumerator<T> GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class FrozenSetExtensions
    {
        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source) => null;
        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source, IEqualityComparer<T> comparer) => null;
    }
}

class Program
{
    private static readonly int[] SeedValues = { 1, 2, 3 };
    private static readonly FrozenSet<int> ReservedIds = SeedValues.ToFrozenSet();
    private static readonly FrozenSet<string> ElevatedRoles = new string[] { ""admin"", ""ops"" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    static bool Matches(int value, string role) => ReservedIds.Contains(value) && ElevatedRoles.Contains(role);
}";

        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            BatchFixedCode = fixedCode,
            NumberOfIncrementalIterations = 2,
            CodeFixTestBehaviors = CodeFixTestBehaviors.SkipLocalDiagnosticCheck,
            CompilerDiagnostics = CompilerDiagnostics.None
        };

        testObj.ExpectedDiagnostics.Add(VerifyFix.Diagnostic("LC033").WithLocation(0).WithArguments("ReservedIds"));
        testObj.ExpectedDiagnostics.Add(VerifyFix.Diagnostic("LC033").WithLocation(1).WithArguments("ElevatedRoles"));

        await testObj.RunAsync();
    }

    [Fact]
    public async Task AliasTypeSyntax_RewritesUsingSemanticElementType()
    {
        var test = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using IntSet = System.Collections.Generic.HashSet<int>;
" + FrozenSupport + @"
class Program
{
    {|#0:private static readonly IntSet ReservedIds = new() { 1, 2, 3 };|}

    static bool IsReserved(int value) => ReservedIds.Contains(value);
}";

        var fixedCode = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using IntSet = System.Collections.Generic.HashSet<int>;
using System.Collections.Frozen;

namespace System.Collections.Frozen
{
    using System.Collections.Generic;

    public abstract class FrozenSet<T> : IEnumerable<T>
    {
        public abstract bool Contains(T item);
        public abstract IEnumerator<T> GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class FrozenSetExtensions
    {
        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source) => null;
        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source, IEqualityComparer<T> comparer) => null;
    }
}

class Program
{
    private static readonly FrozenSet<int> ReservedIds = new int[] { 1, 2, 3 }.ToFrozenSet();

    static bool IsReserved(int value) => ReservedIds.Contains(value);
}";

        await VerifyCodeFixAsync(
            test,
            VerifyFix.Diagnostic("LC033").WithLocation(0).WithArguments("ReservedIds"),
            fixedCode);
    }

    [Fact]
    public async Task CollidingTypeNames_RewritesToTheOriginalElementTypeSymbol()
    {
        var test = @"
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
" + FrozenSupport + @"
namespace Demo.Models
{
    public sealed class Task
    {
    }
}

class Program
{
    {|#0:private static readonly HashSet<Demo.Models.Task> ReservedTasks = new()
    {
        new Demo.Models.Task()
    };|}

    static bool ContainsTask(Demo.Models.Task value) => ReservedTasks.Contains(value);
}";

        var fixedCode = @"
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Frozen;

namespace System.Collections.Frozen
{
    using System.Collections.Generic;

    public abstract class FrozenSet<T> : IEnumerable<T>
    {
        public abstract bool Contains(T item);
        public abstract IEnumerator<T> GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class FrozenSetExtensions
    {
        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source) => null;
        public static FrozenSet<T> ToFrozenSet<T>(this IEnumerable<T> source, IEqualityComparer<T> comparer) => null;
    }
}

namespace Demo.Models
{
    public sealed class Task
    {
    }
}

class Program
{
    private static readonly FrozenSet<Demo.Models.Task> ReservedTasks = new Demo.Models.Task[] {
        new Demo.Models.Task()
    }.ToFrozenSet();

    static bool ContainsTask(Demo.Models.Task value) => ReservedTasks.Contains(value);
}";

        await VerifyCodeFixAsync(
            test,
            VerifyFix.Diagnostic("LC033").WithLocation(0).WithArguments("ReservedTasks"),
            fixedCode);
    }

    [Fact]
    public async Task Fixer_DoesNotRegister_WhenDiagnosticIsNotFixerEligible()
    {
        const string source = """
            using System.Collections.Generic;

            class Program
            {
                private static readonly HashSet<int> ReservedIds = new() { 1, 2, 3 };
            }
            """;

        using var workspace = new AdhocWorkspace();
        var project = workspace.CurrentSolution
            .AddProject("TestProject", "TestProject", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Preview))
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(HashSet<int>).Assembly.Location));

        var document = project.AddDocument("Test.cs", source);
        var root = await document.GetSyntaxRootAsync();
        var fieldDeclaration = root!.DescendantNodes().OfType<FieldDeclarationSyntax>().Single();

        var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor("LC033", "title", "message", "Performance", DiagnosticSeverity.Info, true),
            fieldDeclaration.GetLocation(),
            ImmutableDictionary<string, string?>.Empty);

        var fixer = new LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches.UseFrozenSetForStaticMembershipCachesFixer();
        var actions = new List<CodeAction>();

        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            default);

        await fixer.RegisterCodeFixesAsync(context);

        Assert.Empty(actions);
    }

    private static async Task VerifyCodeFixAsync(string test, DiagnosticResult expected, string fixedCode)
    {
        var testObj = new CodeFixTest
        {
            TestCode = test,
            FixedCode = fixedCode,
            CodeFixTestBehaviors = CodeFixTestBehaviors.SkipLocalDiagnosticCheck,
            CompilerDiagnostics = CompilerDiagnostics.None
        };

        testObj.ExpectedDiagnostics.Add(expected);
        await testObj.RunAsync();
    }

}
