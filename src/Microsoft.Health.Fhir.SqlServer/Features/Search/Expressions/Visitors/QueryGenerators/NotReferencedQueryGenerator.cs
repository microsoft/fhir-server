// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class NotReferencedQueryGenerator : SearchParamTableExpressionQueryGenerator
    {
        public static readonly NotReferencedQueryGenerator Instance = new NotReferencedQueryGenerator();

        public override Table Table => VLatest.Resource;
    }
}
