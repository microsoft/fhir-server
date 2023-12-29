// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class StringQueryGenerator : SearchParamTableExpressionQueryGenerator
    {
        public static readonly StringQueryGenerator Instance = new StringQueryGenerator();

        public override Table Table => VLatest.StringSearchParam;

        public override SearchParameterQueryGeneratorContext VisitString(StringExpression expression, SearchParameterQueryGeneratorContext context)
        {
            StringColumn column;
            switch (expression.FieldName)
            {
                case FieldName.String:
                    column = VLatest.StringSearchParam.Text;
                    break;
                case SqlFieldName.TextOverflow:
                    column = VLatest.StringSearchParam.TextOverflow;
                    AppendColumnName(context, column, expression);
                    context.StringBuilder.Append(" IS NOT NULL AND ");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }

            return VisitSimpleString(expression, context, column, expression.Value);
        }
    }
}
