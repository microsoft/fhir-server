// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
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

        /// <summary>
        /// The conversions below can round a boundary up by one millisecond, so a millisecond of headroom is reserved
        /// below <see cref="ResourceSurrogateIdHelper.MaxDateTime"/> to keep that arithmetic representable.
        /// </summary>
        internal static readonly DateTime MaxSupportedLastUpdated = ResourceSurrogateIdHelper.MaxDateTime.UtcDateTime.AddTicks(-TimeSpan.TicksPerMillisecond);

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

            if (original > MaxSupportedLastUpdated)
            {
                throw new BadRequestException(string.Format(
                    CultureInfo.InvariantCulture,
                    Core.Resources.LastUpdatedValueOutOfRange,
                    MaxSupportedLastUpdated.ToString("o", CultureInfo.InvariantCulture)));
            }

            DateTime truncated = original.TruncateToMillisecond();

            switch (expression.BinaryOperator)
            {
                case BinaryOperator.GreaterThan:
                    return Expression.GreaterThanOrEqual(
                        SqlFieldName.ResourceSurrogateId,
                        null,
                        new DateTimeOffset(truncated.AddTicks(TimeSpan.TicksPerMillisecond)).ToSurrogateId());
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
                        new DateTimeOffset(truncated.AddTicks(TimeSpan.TicksPerMillisecond)).ToSurrogateId());
                case BinaryOperator.NotEqual:
                case BinaryOperator.Equal: // expecting eq to have been rewritten as a range
                default:
                    throw new ArgumentOutOfRangeException(expression.BinaryOperator.ToString());
            }
        }
    }
}
