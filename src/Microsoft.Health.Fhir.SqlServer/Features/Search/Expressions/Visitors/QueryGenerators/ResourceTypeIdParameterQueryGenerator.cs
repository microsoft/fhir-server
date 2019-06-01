// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class ResourceTypeIdParameterQueryGenerator : DenormalizedSearchParameterQueryGenerator
    {
        public override SqlQueryGenerator VisitString(StringExpression expression, SqlQueryGenerator context)
        {
            return VisitSimpleBinary(BinaryOperator.Equal, context, V1.Resource.ResourceTypeId, expression.ComponentIndex, context.Model.GetResourceTypeIdOrInvalidIfNotRecognized(expression.Value));
        }
    }
}