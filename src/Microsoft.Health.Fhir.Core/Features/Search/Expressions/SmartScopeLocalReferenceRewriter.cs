// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Restricts reference predicates that come from SMART clinical scope search parameters to local references.
    /// A relative scope value such as <c>subject=Patient/123</c> names a resource on this server, but a relative
    /// reference search matches on resource type and id only, so it also matches
    /// <c>https://other.server/Patient/123</c>. This rewriter adds a missing base URI condition to those predicates,
    /// producing the same predicate as an absolute reference to this server. Predicates that already constrain the
    /// base URI are left unchanged, and so are negated predicates, where the extra condition would widen the match.
    /// </summary>
    public sealed class SmartScopeLocalReferenceRewriter : ExpressionRewriter<object>
    {
        public static readonly SmartScopeLocalReferenceRewriter Instance = new SmartScopeLocalReferenceRewriter();

        private SmartScopeLocalReferenceRewriter()
        {
        }

        public override Expression VisitNotExpression(NotExpression expression, object context)
        {
            return expression;
        }

        public override Expression VisitMultiary(MultiaryExpression expression, object context)
        {
            if (expression.MultiaryOperation != MultiaryOperator.And)
            {
                return base.VisitMultiary(expression, context);
            }

            // Composite parameters AND several components, so the base URI condition is tracked per component index.
            var componentsWithBaseUri = new HashSet<int?>(expression.Expressions
                .OfType<IFieldExpression>()
                .Where(field => field.FieldName == FieldName.ReferenceBaseUri)
                .Select(field => field.ComponentIndex));

            var componentsToRestrict = new HashSet<int?>(expression.Expressions
                .OfType<StringExpression>()
                .Where(s => s.FieldName == FieldName.ReferenceResourceId && !componentsWithBaseUri.Contains(s.ComponentIndex))
                .Select(s => s.ComponentIndex));

            var rewrittenExpressions = new List<Expression>(expression.Expressions.Count + componentsToRestrict.Count);
            bool changed = false;

            foreach (Expression child in expression.Expressions)
            {
                if (child is StringExpression stringExpression && IsReferenceTypeOrId(stringExpression))
                {
                    // Reference fields of this AND are restricted here, so they are not visited on their own.
                    if (componentsToRestrict.Remove(stringExpression.ComponentIndex))
                    {
                        rewrittenExpressions.Add(Expression.Missing(FieldName.ReferenceBaseUri, stringExpression.ComponentIndex));
                        changed = true;
                    }

                    rewrittenExpressions.Add(child);
                    continue;
                }

                Expression rewrittenChild = child.AcceptVisitor(this, context);
                changed |= !ReferenceEquals(rewrittenChild, child);
                rewrittenExpressions.Add(rewrittenChild);
            }

            return changed ? Expression.And(rewrittenExpressions) : expression;
        }

        public override Expression VisitString(StringExpression expression, object context)
        {
            // An id-only reference value (for example subject=123) is a single string predicate outside an AND.
            return expression.FieldName == FieldName.ReferenceResourceId
                ? Expression.And(Expression.Missing(FieldName.ReferenceBaseUri, expression.ComponentIndex), expression)
                : expression;
        }

        private static bool IsReferenceTypeOrId(StringExpression expression)
        {
            return expression.FieldName == FieldName.ReferenceResourceType || expression.FieldName == FieldName.ReferenceResourceId;
        }
    }
}
