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
    internal class ReferenceSearchParameterQueryGenerator : NormalizedSearchParameterQueryGenerator
    {
        public static readonly ReferenceSearchParameterQueryGenerator Instance = new ReferenceSearchParameterQueryGenerator();

        public override Table Table => VLatest.ReferenceSearchParam;

        public override SearchParameterQueryGeneratorContext VisitString(StringExpression expression, SearchParameterQueryGeneratorContext context)
        {
            switch (expression.FieldName)
            {
                case FieldName.ReferenceBaseUri:
                    return VisitSimpleString(expression, context, VLatest.ReferenceSearchParam.BaseUri, expression.Value);
                case FieldName.ReferenceResourceType:
                    return VisitSimpleBinary(BinaryOperator.Equal, context, VLatest.ReferenceSearchParam.ReferenceResourceTypeId, expression.ComponentIndex, context.Model.GetResourceTypeId(expression.Value));
                case FieldName.ReferenceResourceId:
                    return VisitSimpleString(expression, context, VLatest.ReferenceSearchParam.ReferenceResourceId, expression.Value);
                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }
        }

        public override SearchParameterQueryGeneratorContext VisitMissingField(MissingFieldExpression expression, SearchParameterQueryGeneratorContext context)
        {
            return VisitMissingFieldImpl(expression, context, FieldName.ReferenceBaseUri, VLatest.ReferenceSearchParam.BaseUri);
        }
    }
}
