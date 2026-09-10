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

            // Validate the operator up front so that an unsupported operator is not mistaken for an overflow by the catch block below.
            switch (expression.BinaryOperator)
            {
                case BinaryOperator.GreaterThan:
                case BinaryOperator.GreaterThanOrEqual:
                case BinaryOperator.LessThan:
                case BinaryOperator.LessThanOrEqual:
                    break;
                case BinaryOperator.Equal:
                case BinaryOperator.NotEqual:
                default:
                    throw new ArgumentOutOfRangeException(expression.BinaryOperator.ToString());
            }

            // ResourceSurrogateId has millisecond datetime precision, with lower bits added in to make the value unique.
            DateTime original = ((DateTimeOffset)expression.Value).UtcDateTime;
            DateTime truncated = original.TruncateToMillisecond();

            try
            {
                switch (expression.BinaryOperator)
                {
                    case BinaryOperator.GreaterThan:
                        return Expression.GreaterThanOrEqual(
                            SqlFieldName.ResourceSurrogateId,
                            null,
                            new DateTimeOffset(truncated.SafeAddTicks(TimeSpan.TicksPerMillisecond)).ToSurrogateId());
                    case BinaryOperator.GreaterThanOrEqual:
                        if (original == truncated)
                        {
                            return Expression.GreaterThanOrEqual(
                                SqlFieldName.ResourceSurrogateId,
                                null,
                                new DateTimeOffset(truncated).ToSurrogateId());
                        }

                        goto case BinaryOperator.GreaterThan;
                    case BinaryOperator.LessThan:
                        if (original == truncated)
                        {
                            return Expression.LessThan(
                                SqlFieldName.ResourceSurrogateId,
                                null,
                                new DateTimeOffset(truncated).ToSurrogateId());
                        }

                        goto case BinaryOperator.LessThanOrEqual;
                    case BinaryOperator.LessThanOrEqual:
                        return Expression.LessThan(
                            SqlFieldName.ResourceSurrogateId,
                            null,
                            new DateTimeOffset(truncated.SafeAddTicks(TimeSpan.TicksPerMillisecond)).ToSurrogateId());
                    default:
                        // Unreachable - the operator is validated above.
                        throw new ArgumentOutOfRangeException(expression.BinaryOperator.ToString());
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // If the original/truncated value is outside the representable range of IdHelper.MaxDateTime, constrain it to MaxDateTime.
                return VisitBinaryConstrained(expression);
            }
        }

        private static BinaryExpression VisitBinaryConstrained(BinaryExpression expression)
        {
            // Constrain to MaxDateTime - Dates beyond MaxDateTime are outside the system's representable range.
            // ResourceSurrogateId encodes the datetime in high bits and a uniquifier (0-7) in low 3 bits.
            // Use the max surrogate ID for the constrained millisecond (uniquifier = 7) to correctly account for all rows in that bucket.
            DateTime constrained = IdHelper.MaxDateTime.UtcDateTime;
            long maxSurrogateId = new DateTimeOffset(constrained).ToSurrogateId() + 7;

            switch (expression.BinaryOperator)
            {
                case BinaryOperator.GreaterThan:
                    // GT maxSurrogateId is always-false (no row can exceed the max possible ID)
                    return Expression.GreaterThan(
                        SqlFieldName.ResourceSurrogateId,
                        null,
                        maxSurrogateId);

                case BinaryOperator.GreaterThanOrEqual:
                    // GE maxSurrogateId is always-false
                    return Expression.GreaterThanOrEqual(
                        SqlFieldName.ResourceSurrogateId,
                        null,
                        maxSurrogateId);

                case BinaryOperator.LessThan:
                case BinaryOperator.LessThanOrEqual:
                    // When clamping an overflowed value down, widen LT to LE to be semantically correct.
                    // LT/LE maxSurrogateId is always-true (all rows are <= max possible ID)
                    return Expression.LessThanOrEqual(
                        SqlFieldName.ResourceSurrogateId,
                        null,
                        maxSurrogateId);

                case BinaryOperator.NotEqual:
                case BinaryOperator.Equal: // expecting eq to have been rewritten as a range
                default:
                    throw new ArgumentOutOfRangeException(expression.BinaryOperator.ToString());
            }
        }
    }
}
