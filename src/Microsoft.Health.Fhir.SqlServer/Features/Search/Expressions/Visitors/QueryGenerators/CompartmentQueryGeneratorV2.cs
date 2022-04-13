// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class CompartmentQueryGeneratorV2 : SearchParamTableExpressionQueryGenerator
    {
        public static readonly CompartmentQueryGeneratorV2 Instance = new CompartmentQueryGeneratorV2();

        public override Table Table => VLatest.ReferenceSearchParam;

        public override SearchParameterQueryGeneratorContext VisitCompartment(CompartmentSearchExpression expression, SearchParameterQueryGeneratorContext context)
        {
            byte compartmentTypeId = context.Model.GetCompartmentTypeId(expression.CompartmentType);

            context.StringBuilder
                .Append(VLatest.ReferenceSearchParam.ReferenceResourceId, context.TableAlias)
                .Append(" = ")
                .Append(context.Parameters.AddParameter(VLatest.ReferenceSearchParam.ReferenceResourceId, compartmentTypeId, true));

            return context;
        }
    }
}
