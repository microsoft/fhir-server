// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class RevIncludeQueryGenerator : NormalizedSearchParameterQueryGenerator
    {
        internal static readonly RevIncludeQueryGenerator Instance = new RevIncludeQueryGenerator();

        public override Table Table => null;
    }
}
