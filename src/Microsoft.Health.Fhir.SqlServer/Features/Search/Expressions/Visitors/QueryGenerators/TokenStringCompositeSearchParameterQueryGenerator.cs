// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class TokenStringCompositeSearchParameterQueryGenerator : CompositeSearchParameterQueryGenerator
    {
        public static readonly TokenStringCompositeSearchParameterQueryGenerator Instance = new TokenStringCompositeSearchParameterQueryGenerator();

        public TokenStringCompositeSearchParameterQueryGenerator()
            : base(TokenSearchParameterQueryGenerator.Instance, StringSearchParameterQueryGenerator.Instance)
        {
        }

        public override Table Table => V1.TokenStringCompositeSearchParam;
    }
}