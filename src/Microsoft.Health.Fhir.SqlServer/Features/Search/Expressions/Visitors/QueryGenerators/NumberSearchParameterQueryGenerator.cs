// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class NumberSearchParameterQueryGenerator : NormalizedSearchParameterQueryGenerator
    {
        public static readonly NumberSearchParameterQueryGenerator Instance = new NumberSearchParameterQueryGenerator();

        public override Table Table => V1.NumberSearchParam;

        public override SearchParameterQueryGeneratorContext VisitBinary(BinaryExpression expression, SearchParameterQueryGeneratorContext context)
        {
            NullableDecimalColumn valueColumn;
            NullableDecimalColumn nullCheckColumn;
            switch (expression.FieldName)
            {
                case FieldName.Number:
                    valueColumn = nullCheckColumn = V1.NumberSearchParam.SingleValue;
                    break;
                case SqlFieldName.NumberLow:
                    valueColumn = nullCheckColumn = V1.NumberSearchParam.LowValue;
                    break;
                case SqlFieldName.NumberHigh:
                    valueColumn = V1.NumberSearchParam.HighValue;
                    nullCheckColumn = V1.NumberSearchParam.LowValue;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }

            context.StringBuilder.Append(nullCheckColumn, context.TableAlias).Append(expression.ComponentIndex + 1).Append(" IS NOT NULL AND ");
            return VisitSimpleBinary(expression.BinaryOperator, context, valueColumn, expression.ComponentIndex, expression.Value);
        }
    }
}
