// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class LastUpdatedParameterQueryGenerator : DenormalizedSearchParameterQueryGenerator
    {
        public override SqlQueryGenerator VisitBinary(BinaryExpression expression, SqlQueryGenerator context)
        {
            VisitSimpleBinary(expression.BinaryOperator, context, V1.Resource.LastUpdated, expression.ComponentIndex, ((DateTimeOffset)expression.Value).UtcDateTime);
            return context;
        }
    }
}