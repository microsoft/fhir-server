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
    internal class IdentifierOfTypeQueryGenerator : SearchParamTableExpressionQueryGenerator
    {
        public static readonly IdentifierOfTypeQueryGenerator Instance = new IdentifierOfTypeQueryGenerator();

        public override Table Table => VLatest.IdentifierOfTypeSearchParam;

        public override SearchParameterQueryGeneratorContext VisitString(StringExpression expression, SearchParameterQueryGeneratorContext context)
        {
            Debug.Assert(expression.StringOperator == StringOperator.Equals, "Only equals is supported");

            switch (expression.FieldName)
            {
                case FieldName.IdentifierSystem:
                    if (context.Model.TryGetSystemId(expression.Value, out var systemId))
                    {
                        return VisitSimpleBinary(BinaryOperator.Equal, context, VLatest.IdentifierOfTypeSearchParam.SystemId, expression.ComponentIndex, systemId);
                    }

                    AppendColumnName(context, VLatest.IdentifierOfTypeSearchParam.SystemId, expression)
                        .Append(" IN (SELECT ")
                        .Append(VLatest.System.SystemId, null)
                        .Append(" FROM ").Append(VLatest.System)
                        .Append(" WHERE ")
                        .Append(VLatest.System.Value, null)
                        .Append(" = ")
                        .Append(context.Parameters.AddParameter(VLatest.System.Value, expression.Value, true))
                        .Append(")");

                    return context;

                case FieldName.IdentifierCode:
                    VisitSimpleString(expression, context, VLatest.IdentifierOfTypeSearchParam.Code, expression.Value);
                    break;
                case FieldName.IdentifierValue:
                    VisitSimpleString(expression, context, VLatest.IdentifierOfTypeSearchParam.Value, expression.Value);
                    break;
                default:
                    throw new InvalidOperationException();
            }

            return context;
        }
    }
}
