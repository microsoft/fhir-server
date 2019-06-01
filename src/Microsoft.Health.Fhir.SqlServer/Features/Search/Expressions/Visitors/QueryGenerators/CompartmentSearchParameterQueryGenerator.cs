// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class CompartmentSearchParameterQueryGenerator : NormalizedSearchParameterQueryGenerator
    {
        public static readonly CompartmentSearchParameterQueryGenerator Instance = new CompartmentSearchParameterQueryGenerator();

        public override Table Table => V1.CompartmentAssignment;

        public override SqlQueryGenerator VisitCompartment(CompartmentSearchExpression expression, SqlQueryGenerator context)
        {
            byte compartmentId = context.Model.GetCompartmentId(expression.CompartmentType);

            context.StringBuilder
                .Append(V1.CompartmentAssignment.CompartmentTypeId)
                .Append(" = ")
                .Append(context.Parameters.AddParameter(V1.CompartmentAssignment.CompartmentTypeId, compartmentId))
                .AppendLine()
                .Append("AND ")
                .Append(V1.CompartmentAssignment.ReferenceResourceId)
                .Append(" = ")
                .Append(context.Parameters.AddParameter(V1.CompartmentAssignment.ReferenceResourceId, expression.CompartmentId));

            return context;
        }
    }
}