// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal class TypeConstraintVisitor : DefaultExpressionVisitor<object, bool>
    {
        internal static readonly TypeConstraintVisitor Instance = new();

        public TypeConstraintVisitor()
            : base((a, b) => a | b)
        {
        }

        public override bool VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            if (expression is not { Parameter: { Name: SearchParameterNames.ResourceType } })
            {
                return false;
            }

            return base.VisitSearchParameter(expression, context);
        }

        public override bool VisitMultiary(MultiaryExpression expression, object context)
        {
            if (expression.MultiaryOperation == MultiaryOperator.And)
            {
                return base.VisitMultiary(expression, context);
            }

            // expecting to be within a _type SearchParameterExpression
            return expression.Expressions.Count == 1;
        }

        public override bool VisitString(StringExpression expression, object context) => true;
    }
}
