// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// A rewriter that prevents <see cref="TrustedResourceIdListExpression" /> from being processed
    /// by other rewriters that would apply filters (compartment, smart compartment, scope).
    ///
    /// This rewriter is applied as the outermost rewriter in the chain to protect the trusted
    /// resource ID list from being modified.
    /// </summary>
    internal class PreserveTrustedResourceIdListRewriter : ExpressionRewriter<object>
    {
        public static readonly PreserveTrustedResourceIdListRewriter Instance = new PreserveTrustedResourceIdListRewriter();

        /// <summary>
        /// Override to pass TrustedResourceIdListExpression through unchanged,
        /// preventing it from being further processed.
        /// </summary>
        public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            // If the inner expression is a TrustedResourceIdListExpression, don't process it
            if (expression.Expression is TrustedResourceIdListExpression)
            {
                return expression;
            }

            return base.VisitSearchParameter(expression, context);
        }

        /// <summary>
        /// Override to prevent TrustedResourceIdListExpression from being modified when
        /// it appears in a MultiaryExpression.
        /// </summary>
        public override Expression VisitMultiary(MultiaryExpression expression, object context)
        {
            // Check if any expression is a TrustedResourceIdListExpression
            bool hasTrustedIdList = expression.Expressions.Any(e => e is TrustedResourceIdListExpression);

            if (hasTrustedIdList)
            {
                // Don't process child expressions to protect the trusted ID list
                return expression;
            }

            return base.VisitMultiary(expression, context);
        }

        /// <summary>
        /// TrustedResourceIdListExpression should never be wrapped in other expression types
        /// and should pass through all rewriters unchanged.
        /// </summary>
        public override Expression VisitTrustedResourceIdList(TrustedResourceIdListExpression expression, object context)
        {
            return expression;
        }
    }
}
