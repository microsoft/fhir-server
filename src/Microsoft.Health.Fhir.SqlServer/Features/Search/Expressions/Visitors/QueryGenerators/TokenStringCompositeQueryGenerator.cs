// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class TokenStringCompositeQueryGenerator : CompositeQueryGenerator
    {
        public static readonly TokenStringCompositeQueryGenerator Instance = new TokenStringCompositeQueryGenerator();

        public TokenStringCompositeQueryGenerator()
            : base(TokenQueryGenerator.Instance, StringQueryGenerator.Instance)
        {
        }

        public override Table Table => VLatest.TokenStringCompositeSearchParam;
    }
}
