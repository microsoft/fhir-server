// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Turns predicates over _lastUpdated to be over ResourceSurrogateId
    /// </summary>
    internal class LastUpdatedToResourceSurrogateIdRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        internal static readonly LastUpdatedToResourceSurrogateIdRewriter Instance = new LastUpdatedToResourceSurrogateIdRewriter();

        public override Expression VisitMissingSearchParameter(MissingSearchParameterExpression expression, object context)
        {
            if (expression.Parameter.Code == SearchParameterNames.LastUpdated)
            {
                return Expression.MissingSearchParameter(SqlSearchParameters.ResourceSurrogateIdParameter, expression.IsMissing);
            }

            return expression;
        }

        public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            if (expression.Parameter.Code == SearchParameterNames.LastUpdated)
            {
                return Expression.SearchParameter(SqlSearchParameters.ResourceSurrogateIdParameter, expression.Expression.AcceptVisitor(this, context));
            }

            return expression;
        }

        public override Expression VisitBinary(BinaryExpression expression, object context)
        {
            if (expression.FieldName != FieldName.DateTimeStart && expression.FieldName != FieldName.DateTimeEnd)
            {
                throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }

            // ResourceSurrogateId has millisecond datetime precision, with lower bits added in to make the value unique.

            DateTime original = ((DateTimeOffset)expression.Value).UtcDateTime;
            DateTime truncated = original.TruncateToMillisecond();

            // Constrain to MaxDateTime to prevent overflow during DateTime arithmetic.
            // Dates beyond MaxDateTime are outside the system's representable range.
            DateTime constrained = truncated > IdHelper.MaxDateTime.UtcDateTime
                ? IdHelper.MaxDateTime.UtcDateTime
                : truncated;

            switch (expression.BinaryOperator)
            {
                case BinaryOperator.GreaterThan:
                    // For gt, round up to next millisecond, clamped to MaxDateTime
                    DateTime gt = constrained.SafeAddTicks(TimeSpan.TicksPerMillisecond);
                    if (gt > IdHelper.MaxDateTime.UtcDateTime)
                    {
                        gt = IdHelper.MaxDateTime.UtcDateTime;
                    }

                    return Expression.GreaterThanOrEqual(
                        SqlFieldName.ResourceSurrogateId,
                        null,
                        new DateTimeOffset(gt).ToSurrogateId());
                case BinaryOperator.GreaterThanOrEqual:
                    if (original == truncated)
                    {
                        return Expression.GreaterThanOrEqual(
                            SqlFieldName.ResourceSurrogateId,
                            null,
                            new DateTimeOffset(constrained).ToSurrogateId());
                    }

                    goto case BinaryOperator.GreaterThan;
                case BinaryOperator.LessThan:
                    if (original == truncated)
                    {
                        return Expression.LessThan(
                            SqlFieldName.ResourceSurrogateId,
                            null,
                            new DateTimeOffset(constrained).ToSurrogateId());
                    }

                    goto case BinaryOperator.LessThanOrEqual;
                case BinaryOperator.LessThanOrEqual:
                    // For le, round up to next millisecond, clamped to MaxDateTime
                    DateTime lt = constrained.SafeAddTicks(TimeSpan.TicksPerMillisecond);
                    if (lt > IdHelper.MaxDateTime.UtcDateTime)
                    {
                        lt = IdHelper.MaxDateTime.UtcDateTime;
                    }

                    return Expression.LessThan(
                        SqlFieldName.ResourceSurrogateId,
                        null,
                        new DateTimeOffset(lt).ToSurrogateId());
                case BinaryOperator.NotEqual:
                case BinaryOperator.Equal: // expecting eq to have been rewritten as a range
                default:
                    throw new ArgumentOutOfRangeException(expression.BinaryOperator.ToString());
            }
        }
    }
}
