﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class NumberQueryGenerator : SearchParamTableExpressionQueryGenerator
    {
        public static readonly NumberQueryGenerator Instance = new NumberQueryGenerator();

        public override Table Table => VLatest.NumberSearchParam;

        public override SearchParameterQueryGeneratorContext VisitBinary(BinaryExpression expression, SearchParameterQueryGeneratorContext context)
        {
            NullableDecimalColumn valueColumn = null;
            NullableDecimalColumn nullCheckColumn = null;
            DecimalColumn notNullablevalueColumn = null;

            switch (expression.FieldName)
            {
                case FieldName.Number:
                    valueColumn = nullCheckColumn = VLatest.NumberSearchParam.SingleValue;
                    break;
                case SqlFieldName.NumberLow:
                    notNullablevalueColumn = VLatest.NumberSearchParam.LowValue;
                    break;
                case SqlFieldName.NumberHigh:
                    notNullablevalueColumn = VLatest.NumberSearchParam.HighValue;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }

            if (nullCheckColumn != null)
            {
                AppendColumnName(context, nullCheckColumn, expression).Append(" IS NOT NULL AND ");
            }

            if (valueColumn != null)
            {
                return VisitSimpleBinary(expression.BinaryOperator, context, valueColumn, expression.ComponentIndex, expression.Value);
            }

            return VisitSimpleBinary(expression.BinaryOperator, context, notNullablevalueColumn, expression.ComponentIndex, expression.Value);
        }
    }
}
