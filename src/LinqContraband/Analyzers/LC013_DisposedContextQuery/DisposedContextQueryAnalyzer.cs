using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC013_DisposedContextQuery
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class DisposedContextQueryAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "LC013";
        private static readonly LocalizableString Title = "Disposed Context Query";
        private static readonly LocalizableString MessageFormat = "The query is built from DbContext '{0}' which is disposed before enumeration. Materialize before returning.";
        private static readonly LocalizableString Description = "Returning a deferred query from a disposed context causes runtime errors.";
        private const string Category = "Reliability";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            
            // Register operation action
            context.RegisterOperationAction(AnalyzeReturn, OperationKind.Return);
        }

        private void AnalyzeReturn(OperationAnalysisContext context)
        {
            var returnOp = (IReturnOperation)context.Operation;
            var returnedValue = returnOp.ReturnedValue;

            if (returnedValue == null) return;

            // 1. Check if return type is deferred
            if (!IsDeferredType(returnedValue.Type)) return;

            // 2. Find the root source of the query
            var root = GetRootOperation(returnedValue);

            // 3. Check if root is a local variable declared with 'using'
            if (root is ILocalReferenceOperation localRef)
            {
                if (IsDisposedLocal(localRef.Local))
                {
                     context.ReportDiagnostic(Diagnostic.Create(Rule, returnedValue.Syntax.GetLocation(), localRef.Local.Name));
                }
            }
        }

        private bool IsDeferredType(ITypeSymbol? type)
        {
            if (type == null) return false;

            return ImplementsInterface(type, "System.Linq.IQueryable`1") ||
                   ImplementsInterface(type, "System.Collections.Generic.IAsyncEnumerable`1") ||
                   ImplementsInterface(type, "System.Linq.IOrderedQueryable`1") ||
                   ImplementsInterface(type, "System.Linq.IQueryable");
        }
        
        private bool ImplementsInterface(ITypeSymbol type, string interfaceMetadataName)
        {
            if (GetFullMetadataName(type) == interfaceMetadataName)
                return true;
                
            foreach (var i in type.AllInterfaces)
            {
                if (GetFullMetadataName(i) == interfaceMetadataName)
                    return true;
            }
            return false;
        }
        
        private string GetFullMetadataName(ITypeSymbol type)
        {
             return $"{type.ContainingNamespace}.{type.MetadataName}";
        }

        private IOperation GetRootOperation(IOperation operation)
        {
            var current = operation;
            while (true)
            {
                if (current is IConversionOperation conv)
                {
                    current = conv.Operand;
                    continue;
                }
                if (current is IInvocationOperation invoc)
                {
                    // If extension method, first arg is 'this'.
                    if (invoc.TargetMethod.IsExtensionMethod && invoc.Arguments.Length > 0)
                    {
                        current = invoc.Arguments[0].Value;
                        continue;
                    }
                    // If instance method, Instance is the target.
                    if (invoc.Instance != null)
                    {
                        current = invoc.Instance;
                        continue;
                    }
                }
                if (current is IMemberReferenceOperation member)
                {
                    if (member.Instance != null)
                    {
                        current = member.Instance;
                        continue;
                    }
                }
                // ArgumentOperation wrapping the value
                if (current is IArgumentOperation arg)
                {
                    current = arg.Value;
                    continue;
                }

                break;
            }
            return current;
        }

        private bool IsDisposedLocal(ILocalSymbol local)
        {
            foreach (var syntaxRef in local.DeclaringSyntaxReferences)
            {
                var syntax = syntaxRef.GetSyntax();
                
                if (syntax is VariableDeclaratorSyntax declarator)
                {
                    // Case 1: using var x = ...; (LocalDeclarationStatement)
                    if (declarator.Parent is VariableDeclarationSyntax declaration &&
                        declaration.Parent is LocalDeclarationStatementSyntax localDecl)
                    {
                        if (!localDecl.UsingKeyword.IsKind(SyntaxKind.None)) 
                        {
                            return true;
                        }
                    }
                    
                    // Case 2: using (var x = ...) { } (UsingStatement)
                    if (declarator.Parent is VariableDeclarationSyntax declaration2 &&
                        declaration2.Parent is UsingStatementSyntax)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
