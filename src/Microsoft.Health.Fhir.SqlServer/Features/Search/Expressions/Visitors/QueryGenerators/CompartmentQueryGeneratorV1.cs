﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class CompartmentQueryGeneratorV1 : SearchParamTableExpressionQueryGenerator
    {
        public static readonly CompartmentQueryGeneratorV1 Instance = new CompartmentQueryGeneratorV1();

        public override Table Table => VLatest.CompartmentAssignment;

        public override SearchParameterQueryGeneratorContext VisitCompartment(CompartmentSearchExpression expression, SearchParameterQueryGeneratorContext context)
        {
            byte compartmentTypeId = context.Model.GetCompartmentTypeId(expression.CompartmentType);

            context.StringBuilder
                .Append(VLatest.CompartmentAssignment.CompartmentTypeId, context.TableAlias)
                .Append(" = ")
                .Append(context.Parameters.AddParameter(VLatest.CompartmentAssignment.CompartmentTypeId, compartmentTypeId, true))
                .AppendLine()
                .Append("AND ")
                .Append(VLatest.CompartmentAssignment.ReferenceResourceId, context.TableAlias)
                .Append(" = ")
                .Append(context.Parameters.AddParameter(VLatest.CompartmentAssignment.ReferenceResourceId, expression.CompartmentId, true));

            return context;
        }
    }
}
