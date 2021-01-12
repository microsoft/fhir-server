// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class ReferenceTokenCompositeQueryGenerator : CompositeQueryGenerator
    {
        public static readonly ReferenceTokenCompositeQueryGenerator Instance = new ReferenceTokenCompositeQueryGenerator();

        public ReferenceTokenCompositeQueryGenerator()
            : base(ReferenceQueryGenerator.Instance, TokenQueryGenerator.Instance)
        {
        }

        public override Table Table => VLatest.ReferenceTokenCompositeSearchParam;
    }
}
