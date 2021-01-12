// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class TokenTokenCompositeQueryGenerator : CompositeQueryGenerator
    {
        public static readonly TokenTokenCompositeQueryGenerator Instance = new TokenTokenCompositeQueryGenerator();

        public TokenTokenCompositeQueryGenerator()
            : base(TokenQueryGenerator.Instance, TokenQueryGenerator.Instance)
        {
        }

        public override Table Table => VLatest.TokenTokenCompositeSearchParam;
    }
}
