// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class ReferenceQueryGenerator : SearchParamTableExpressionQueryGenerator
    {
        public static readonly ReferenceQueryGenerator Instance = new ReferenceQueryGenerator();

        public override Table Table => VLatest.ReferenceSearchParam;

        public override SearchParameterQueryGeneratorContext VisitString(StringExpression expression, SearchParameterQueryGeneratorContext context)
        {
            switch (expression.FieldName)
            {
                case FieldName.ReferenceBaseUri:
                    return VisitSimpleString(expression, context, VLatest.ReferenceSearchParam.BaseUri, expression.Value);
                case FieldName.ReferenceResourceType:
                    if (context.Model.TryGetResourceTypeId(expression.Value, out short resourceTypeId))
                    {
                        return VisitSimpleBinary(BinaryOperator.Equal, context, VLatest.ReferenceSearchParam.ReferenceResourceTypeId, expression.ComponentIndex, resourceTypeId);
                    }

                    // Resource type not in model info provider (e.g., Citation is a search param target in R5 but excluded from supported resources).
                    // essentially a bug in R5, some search parameters reference target types which are not in the model info provider.
                    context.StringBuilder.Append("0 = 1");
                    return context;
                case FieldName.ReferenceResourceId:
                    return VisitSimpleString(expression, context, VLatest.ReferenceSearchParam.ReferenceResourceId, expression.Value);
                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }
        }

        public override SearchParameterQueryGeneratorContext VisitMissingField(MissingFieldExpression expression, SearchParameterQueryGeneratorContext context)
        {
            switch (expression.FieldName)
            {
                case FieldName.ReferenceBaseUri:
                    return VisitMissingFieldImpl(expression, context, FieldName.ReferenceBaseUri, VLatest.ReferenceSearchParam.BaseUri);
                case FieldName.ReferenceResourceType:
                    return VisitMissingFieldImpl(expression, context, FieldName.ReferenceResourceType, VLatest.ReferenceSearchParam.ReferenceResourceTypeId);
                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }
        }
    }
}
