// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class InQueryGenerator : SearchParamTableExpressionQueryGenerator
    {
        internal static readonly InQueryGenerator Instance = new InQueryGenerator();

        public override Table Table => null;
    }
}
