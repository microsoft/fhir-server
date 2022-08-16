// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class TokenQueryGenerator : SearchParamTableExpressionQueryGenerator
    {
        public static readonly TokenQueryGenerator Instance = new TokenQueryGenerator();

        public override Table Table => VLatest.TokenSearchParam;

        public override SearchParameterQueryGeneratorContext VisitMissingField(MissingFieldExpression expression, SearchParameterQueryGeneratorContext context)
        {
            return VisitMissingFieldImpl(expression, context, FieldName.TokenSystem, VLatest.TokenSearchParam.SystemId);
        }

        public override SearchParameterQueryGeneratorContext VisitString(StringExpression expression, SearchParameterQueryGeneratorContext context)
        {
            Debug.Assert(expression.StringOperator == StringOperator.Equals, "Only equals is supported");

            switch (expression.FieldName)
            {
                case FieldName.TokenSystem:
                    if (context.Model.TryGetSystemId(expression.Value, out var systemId))
                    {
                        return VisitSimpleBinary(BinaryOperator.Equal, context, VLatest.TokenSearchParam.SystemId, expression.ComponentIndex, systemId);
                    }

                    AppendColumnName(context, VLatest.TokenSearchParam.SystemId, expression)
                        .Append(" IN (SELECT ")
                        .Append(VLatest.System.SystemId, null)
                        .Append(" FROM ").Append(VLatest.System)
                        .Append(" WHERE ")
                        .Append(VLatest.System.Value, null)
                        .Append(" = ")
                        .Append(context.Parameters.AddParameter(VLatest.System.Value, expression.Value, true))
                        .Append(")");

                    return context;

                case FieldName.TokenCode:
                    if (expression.Value.Length <= VLatest.TokenSearchParam.Code.Metadata.MaxLength)
                    {
                        VisitSimpleString(expression, context, VLatest.TokenSearchParam.Code, expression.Value);
                        context.StringBuilder.Append(" AND ");
                        AppendColumnName(context, VLatest.TokenSearchParam.CodeOverflow, expression);
                        context.StringBuilder.Append(" IS NULL");
                    }
                    else
                    {
                        int codeLength;
                        checked
                        {
                            codeLength = (int)VLatest.TokenSearchParam.Code.Metadata.MaxLength; // Throw overflow if code max lenght is ever too big to fit into int.
                        }

                        VisitSimpleString(expression, context, VLatest.TokenSearchParam.Code, expression.Value[..codeLength]);
                        context.StringBuilder.Append(" AND ");
                        AppendColumnName(context, VLatest.TokenSearchParam.CodeOverflow, expression);
                        context.StringBuilder.Append(" IS NOT NULL AND ");
                        VisitSimpleString(expression, context, VLatest.TokenSearchParam.CodeOverflow, expression.Value[codeLength..]);
                    }

                    break;
                default:
                    throw new InvalidOperationException();
            }

            return context;
        }
    }
}
