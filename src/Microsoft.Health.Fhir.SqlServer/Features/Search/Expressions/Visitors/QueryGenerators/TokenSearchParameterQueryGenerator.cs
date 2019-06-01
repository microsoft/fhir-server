// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class TokenSearchParameterQueryGenerator : NormalizedSearchParameterQueryGenerator
    {
        public static readonly TokenSearchParameterQueryGenerator Instance = new TokenSearchParameterQueryGenerator();

        public override Table Table => V1.TokenSearchParam;

        public override SqlQueryGenerator VisitMissingField(MissingFieldExpression expression, SqlQueryGenerator context)
        {
            return VisitMissingFieldImpl(expression, context, FieldName.TokenSystem, V1.TokenSearchParam.SystemId);
        }

        public override SqlQueryGenerator VisitString(StringExpression expression, SqlQueryGenerator context)
        {
            Debug.Assert(expression.StringOperator == StringOperator.Equals, "Only equals is supported");

            switch (expression.FieldName)
            {
                case FieldName.TokenSystem:
                    VisitSimpleBinary(BinaryOperator.Equal, context, V1.TokenSearchParam.SystemId, expression.ComponentIndex, context.Model.GetSystem(expression.Value));
                    break;
                case FieldName.TokenCode:
                    VisitSimpleString(expression, context, V1.TokenSearchParam.Code, expression.Value);
                    break;
                default:
                    throw new InvalidOperationException();
            }

            return context;
        }
    }
}
