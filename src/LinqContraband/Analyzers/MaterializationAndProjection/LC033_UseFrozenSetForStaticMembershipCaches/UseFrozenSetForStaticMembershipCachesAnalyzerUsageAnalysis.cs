using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches;

public sealed partial class UseFrozenSetForStaticMembershipCachesAnalyzer
{
    private sealed partial class AnalysisState
    {
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
}
