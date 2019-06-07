// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class TokenNumberNumberSearchParameterQueryGenerator : CompositeSearchParameterQueryGenerator
    {
        public static readonly TokenNumberNumberSearchParameterQueryGenerator Instance = new TokenNumberNumberSearchParameterQueryGenerator();

        public TokenNumberNumberSearchParameterQueryGenerator()
            : base(TokenSearchParameterQueryGenerator.Instance, NumberSearchParameterQueryGenerator.Instance, NumberSearchParameterQueryGenerator.Instance)
        {
        }

        public override Table Table => V1.TokenStringCompositeSearchParam;
    }
}