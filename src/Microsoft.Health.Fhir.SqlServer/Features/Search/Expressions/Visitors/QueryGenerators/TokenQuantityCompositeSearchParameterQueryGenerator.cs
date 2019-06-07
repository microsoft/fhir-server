// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class TokenQuantityCompositeSearchParameterQueryGenerator : CompositeSearchParameterQueryGenerator
    {
        public static readonly TokenQuantityCompositeSearchParameterQueryGenerator Instance = new TokenQuantityCompositeSearchParameterQueryGenerator();

        public TokenQuantityCompositeSearchParameterQueryGenerator()
            : base(TokenSearchParameterQueryGenerator.Instance, QuantitySearchParameterQueryGenerator.Instance)
        {
        }

        public override Table Table => V1.TokenQuantityCompositeSearchParam;
    }
}