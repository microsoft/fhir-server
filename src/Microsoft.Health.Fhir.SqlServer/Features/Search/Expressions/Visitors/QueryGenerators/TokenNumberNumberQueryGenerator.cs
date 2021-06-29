// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class TokenNumberNumberQueryGenerator : CompositeQueryGenerator
    {
        public static readonly TokenNumberNumberQueryGenerator Instance = new TokenNumberNumberQueryGenerator();

        public TokenNumberNumberQueryGenerator()
            : base(TokenQueryGenerator.Instance, NumberQueryGenerator.Instance, NumberQueryGenerator.Instance)
        {
        }

        public override Table Table => VLatest.TokenNumberNumberCompositeSearchParam;
    }
}
