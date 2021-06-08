// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Looks for _type parameters to determine the set of ResourceTypeIds allowed in an expression.
    /// Assumes that <see cref="ResourceColumnPredicatePushdownRewriter"/> has not run yet on the expression.
    /// </summary>
    internal class TypeConstraintVisitor : DefaultSqlExpressionVisitor<(BitArray allowedTypes, ISqlServerFhirModel model), short?>
    {
        private const short NoTypes = -1;

        internal static readonly TypeConstraintVisitor Instance = new();

        /// <summary>
        /// Determines which resource types are allowed in an expression.
        /// </summary>
        /// <param name="expression">The expression to visit.</param>
        /// <param name="model">The model instance.</param>
        /// <returns>A tuple with the single allowed resource type id if there is exactly one (otherwise null) and
        /// a <see cref="BitArray"/> with bits set for each resource type that is allowed, or null if no types are allowed.</returns>
        public (short? singleAllowedResourceTypeId, BitArray allAllowedTypes) Visit(Expression expression, ISqlServerFhirModel model)
        {
            var allowedTypes = new BitArray(model.ResourceTypeIdRange.highestId + 1, true);
            for (int i = 0; i < model.ResourceTypeIdRange.lowestId; i++)
            {
                allowedTypes[i] = false;
            }

            short? singleResourceTypeId = expression?.AcceptVisitor(this, (allowedTypes, model));
            return singleResourceTypeId == NoTypes ? (null, null) : (singleResourceTypeId, allowedTypes);
        }

        public override short? VisitSearchParameter(SearchParameterExpression expression, (BitArray allowedTypes, ISqlServerFhirModel model) context)
        {
            if (expression is { Parameter: { Name: SearchParameterNames.ResourceType } })
            {
                return base.VisitSearchParameter(expression, context);
            }

            return null;
        }

        public override short? VisitSqlRoot(SqlRootExpression expression, (BitArray allowedTypes, ISqlServerFhirModel model) context)
        {
            EnsureArg.IsNotNull(context.allowedTypes, nameof(context.allowedTypes));
            EnsureArg.IsNotNull(context.model, nameof(context.model));

            return HandleAndedExpressions(expression.ResourceTableExpressions, context);
        }

        public override short? VisitMultiary(MultiaryExpression expression, (BitArray allowedTypes, ISqlServerFhirModel model) context)
        {
            EnsureArg.IsNotNull(context.allowedTypes, nameof(context.allowedTypes));
            EnsureArg.IsNotNull(context.model, nameof(context.model));

            if (expression.MultiaryOperation == MultiaryOperator.And)
            {
                return HandleAndedExpressions(expression.Expressions, context);
            }

            // assuming this OR to be within a _type SearchParameterExpression

            var orArray = new BitArray(context.model.ResourceTypeIdRange.highestId + 1, false);
            foreach (Expression childExpression in expression.Expressions)
            {
                // pass null as the bitarray to save on allocations and rely on the return parameter.
                short? single = childExpression.AcceptVisitor(this, (null, context.model));
                if (single is > 0)
                {
                    orArray[single.Value] = true;
                }
            }

            context.allowedTypes.And(orArray);

            int allowedTypesCount = 0;
            short lastAllowedType = 0;
            for (short i = 0; i < context.allowedTypes.Count; i++)
            {
                if (context.allowedTypes[i])
                {
                    allowedTypesCount++;
                    lastAllowedType = i;
                }
            }

            return allowedTypesCount switch { 0 => NoTypes, 1 => lastAllowedType, _ => null};
        }

        private short? HandleAndedExpressions(IReadOnlyList<Expression> expressions, (BitArray allowedTypes, ISqlServerFhirModel model) context)
        {
            short? overallResult = null;
            foreach (Expression childExpression in expressions)
            {
                short? result = childExpression.AcceptVisitor(this, context);
                if (result != null)
                {
                    overallResult = result;
                }
            }

            return overallResult;
        }

        public override short? VisitString(StringExpression expression, (BitArray allowedTypes, ISqlServerFhirModel model) context)
        {
            EnsureArg.IsNotNull(context.model, nameof(context.model));

            short resourceTypeId = context.model.GetResourceTypeId(expression.Value);

            if (context.allowedTypes != null)
            {
                bool isTypeCurrentlyAllowed = context.allowedTypes[resourceTypeId];
                context.allowedTypes.SetAll(false);
                if (isTypeCurrentlyAllowed)
                {
                    context.allowedTypes[resourceTypeId] = true;
                    return resourceTypeId;
                }

                return NoTypes;
            }

            return resourceTypeId;
        }
    }
}
