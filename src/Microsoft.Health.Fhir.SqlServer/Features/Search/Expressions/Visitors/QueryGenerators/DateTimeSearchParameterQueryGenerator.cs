// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class DateTimeSearchParameterQueryGenerator : NormalizedSearchParameterQueryGenerator
    {
        public static readonly DateTimeSearchParameterQueryGenerator Instance = new DateTimeSearchParameterQueryGenerator();

        public override Table Table => V1.DateTimeSearchParam;

        public override SqlQueryGenerator VisitBinary(BinaryExpression expression, SqlQueryGenerator context)
        {
            DateTime2Column column;
            switch (expression.FieldName)
            {
                case FieldName.DateTimeStart:
                    column = V1.DateTimeSearchParam.StartDateTime;
                    break;
                case FieldName.DateTimeEnd:
                    column = V1.DateTimeSearchParam.EndDateTime;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }

            return VisitSimpleBinary(expression.BinaryOperator, context, column, expression.ComponentIndex, ((DateTimeOffset)expression.Value).UtcDateTime);
        }
    }
}