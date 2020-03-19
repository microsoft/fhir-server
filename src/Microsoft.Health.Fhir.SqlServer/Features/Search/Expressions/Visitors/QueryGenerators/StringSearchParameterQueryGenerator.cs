// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class StringSearchParameterQueryGenerator : NormalizedSearchParameterQueryGenerator
    {
        public static readonly StringSearchParameterQueryGenerator Instance = new StringSearchParameterQueryGenerator();

        public override Table Table => VLatest.StringSearchParam;

        public override SearchParameterQueryGeneratorContext VisitString(StringExpression expression, SearchParameterQueryGeneratorContext context)
        {
            AppendColumnName(context, VLatest.StringSearchParam.TextOverflow, expression);

            StringColumn column;
            switch (expression.FieldName)
            {
                case FieldName.String:
                    column = VLatest.StringSearchParam.Text;
                    context.StringBuilder.Append(" IS NULL AND ");
                    break;
                case SqlFieldName.TextOverflow:
                    column = VLatest.StringSearchParam.TextOverflow;
                    switch (expression.StringOperator)
                    {
                        case StringOperator.StartsWith:
                        case StringOperator.NotStartsWith:
                        case StringOperator.Equals:
                            if (expression.Value.Length <= VLatest.StringSearchParam.Text.Metadata.MaxLength)
                            {
                                column = VLatest.StringSearchParam.Text;
                            }

                            break;
                    }

                    context.StringBuilder.Append(" IS NOT NULL AND ");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }

            return VisitSimpleString(expression, context, column, expression.Value);
        }
    }
}
