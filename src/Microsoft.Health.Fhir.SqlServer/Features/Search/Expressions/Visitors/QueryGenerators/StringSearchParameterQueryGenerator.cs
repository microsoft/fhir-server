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

        public override Table Table => V1.StringSearchParam;

        public override SearchParameterQueryGeneratorContext VisitString(StringExpression expression, SearchParameterQueryGeneratorContext context)
        {
            context.StringBuilder.Append(V1.StringSearchParam.TextOverflow, context.TableAlias).Append(expression.ComponentIndex + 1);

            StringColumn column;
            switch (expression.FieldName)
            {
                case FieldName.String:
                    column = V1.StringSearchParam.Text;
                    context.StringBuilder.Append(" IS NULL AND ");
                    break;
                case SqlFieldName.TextOverflow:
                    column = V1.StringSearchParam.TextOverflow;
                    switch (expression.StringOperator)
                    {
                        case StringOperator.StartsWith:
                        case StringOperator.NotStartsWith:
                        case StringOperator.Equals:
                            if (expression.Value.Length <= V1.StringSearchParam.Text.Metadata.MaxLength)
                            {
                                column = V1.StringSearchParam.Text;
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
