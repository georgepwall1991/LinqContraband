using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseFrozenSetForStaticMembershipCachesAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC033";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Use FrozenSet for provably read-only membership caches";

    private static readonly LocalizableString MessageFormat =
        "Field '{0}' is a provably read-only membership cache. Consider FrozenSet<T> for faster steady-state Contains lookups on .NET 8+.";

    private static readonly LocalizableString Description =
        "Reports only when a private static readonly HashSet<T> has a fixer-safe initializer and every source reference is a direct Contains call outside IQueryable or expression-tree contexts.";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description,
        helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC033_UseFrozenSetForStaticMembershipCaches.md",
        customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(InitializeCompilation);
    }

    private static void InitializeCompilation(CompilationStartAnalysisContext context)
    {
        if (!UseFrozenSetForStaticMembershipCachesAnalysis.TryGetFrozenSetSupport(context.Compilation, out var support))
            return;

        var state = new AnalysisState(context.Compilation, support);
        context.RegisterSymbolAction(state.AnalyzeField, SymbolKind.Field);
        context.RegisterOperationAction(state.AnalyzeFieldReference, OperationKind.FieldReference);
        context.RegisterCompilationEndAction(state.ReportDiagnostics);
    }

    private sealed class AnalysisState
    {
        private readonly Compilation _compilation;
        private readonly FrozenSetSupport _support;
        private readonly ConcurrentDictionary<IFieldSymbol, CandidateField> _candidates =
            new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<IFieldSymbol, int> _allowedUsageCounts =
            new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<IFieldSymbol, byte> _disallowedUsages =
            new(SymbolEqualityComparer.Default);

        public AnalysisState(Compilation compilation, FrozenSetSupport support)
        {
            _compilation = compilation;
            _support = support;
        }

        public void AnalyzeField(SymbolAnalysisContext context)
        {
            var field = (IFieldSymbol)context.Symbol;
            if (!IsPotentialCandidate(field))
                return;

            if (!TryGetSingleFieldDeclaration(field, context.CancellationToken, out var fieldDeclaration))
                return;

            var declarator = fieldDeclaration.Declaration.Variables[0];
            if (declarator.Initializer?.Value is not ExpressionSyntax initializerSyntax)
                return;

            var semanticModel = _compilation.GetSemanticModel(initializerSyntax.SyntaxTree);
            if (!UseFrozenSetForStaticMembershipCachesAnalysis.TryClassifyInitializer(
                    initializerSyntax,
                    semanticModel,
                    _support,
                    context.CancellationToken,
                    out _))
            {
                return;
            }

            _candidates.TryAdd(field, new CandidateField(field, fieldDeclaration.GetLocation()));
        }

        public void AnalyzeFieldReference(OperationAnalysisContext context)
        {
            var fieldReference = (IFieldReferenceOperation)context.Operation;
            var field = fieldReference.Field;

            if (!IsPotentialCandidate(field))
                return;

            if (IsAllowedContainsUsage(fieldReference))
            {
                _allowedUsageCounts.AddOrUpdate(field, 1, static (_, count) => count + 1);
                return;
            }

            _disallowedUsages.TryAdd(field, 0);
        }

        public void ReportDiagnostics(CompilationAnalysisContext context)
        {
            var properties = ImmutableDictionary<string, string?>.Empty.Add(
                UseFrozenSetForStaticMembershipCachesDiagnosticProperties.FixerEligible,
                "true");

            foreach (var pair in _candidates)
            {
                var field = pair.Key;
                var candidate = pair.Value;

                if (_disallowedUsages.ContainsKey(field))
                    continue;

                if (!_allowedUsageCounts.TryGetValue(field, out var allowedCount) || allowedCount == 0)
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(Rule, candidate.Location, properties, field.Name));
            }
        }

        private bool IsPotentialCandidate(IFieldSymbol field)
        {
            return field.Locations.Any(static location => location.IsInSource) &&
                   field.DeclaredAccessibility == Accessibility.Private &&
                   field.IsStatic &&
                   field.IsReadOnly &&
                   UseFrozenSetForStaticMembershipCachesAnalysis.IsHashSetType(field.Type, _support.HashSetType);
        }

        private bool TryGetSingleFieldDeclaration(
            IFieldSymbol field,
            System.Threading.CancellationToken cancellationToken,
            out FieldDeclarationSyntax fieldDeclaration)
        {
            fieldDeclaration = null!;

            if (field.DeclaringSyntaxReferences.Length != 1 ||
                field.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not VariableDeclaratorSyntax declarator ||
                declarator.Parent?.Parent is not FieldDeclarationSyntax declaration)
            {
                return false;
            }

            if (declaration.Declaration.Variables.Count != 1)
                return false;

            fieldDeclaration = declaration;
            return true;
        }

        private bool IsAllowedContainsUsage(IFieldReferenceOperation fieldReference)
        {
            if (fieldReference.Parent is not IInvocationOperation invocation)
                return false;

            if (invocation.TargetMethod.Name != "Contains" ||
                invocation.TargetMethod.IsExtensionMethod ||
                invocation.Arguments.Length != 1 ||
                invocation.Type?.SpecialType != SpecialType.System_Boolean)
            {
                return false;
            }

            if (invocation.Instance?.UnwrapConversions() is not IFieldReferenceOperation receiver ||
                !SymbolEqualityComparer.Default.Equals(receiver.Field, fieldReference.Field))
            {
                return false;
            }

            return !IsInExpressionTree(invocation);
        }

        private bool IsInExpressionTree(IOperation operation)
        {
            for (var current = operation; current != null; current = current.Parent)
            {
                if (current is not IAnonymousFunctionOperation anonymousFunction)
                    continue;

                var parent = anonymousFunction.Parent;
                while (parent is IConversionOperation or IDelegateCreationOperation or IParenthesizedOperation)
                {
                    if (UseFrozenSetForStaticMembershipCachesAnalysis.IsExpressionType(parent.Type, _support.ExpressionType))
                        return true;

                    parent = parent.Parent;
                }

                if (UseFrozenSetForStaticMembershipCachesAnalysis.IsExpressionType(parent?.Type, _support.ExpressionType))
                    return true;
            }

            return false;
        }
    }

    private sealed class CandidateField
    {
        public CandidateField(IFieldSymbol field, Location location)
        {
            Field = field;
            Location = location;
        }

        public IFieldSymbol Field { get; }
        public Location Location { get; }
    }
}
