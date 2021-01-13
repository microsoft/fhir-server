// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class ChainLinkQueryGenerator : SearchParamTableExpressionQueryGenerator
    {
        internal static readonly ChainLinkQueryGenerator Instance = new ChainLinkQueryGenerator();

        public override Table Table => null;
    }
}
