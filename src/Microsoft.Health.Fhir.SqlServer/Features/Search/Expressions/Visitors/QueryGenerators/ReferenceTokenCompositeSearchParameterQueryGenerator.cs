// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class ReferenceTokenCompositeSearchParameterQueryGenerator : CompositeSearchParameterQueryGenerator
    {
        public static readonly ReferenceTokenCompositeSearchParameterQueryGenerator Instance = new ReferenceTokenCompositeSearchParameterQueryGenerator();

        public ReferenceTokenCompositeSearchParameterQueryGenerator()
            : base(ReferenceSearchParameterQueryGenerator.Instance, TokenSearchParameterQueryGenerator.Instance)
        {
        }

        public override Table Table => V1.ReferenceTokenCompositeSearchParam;
    }
}