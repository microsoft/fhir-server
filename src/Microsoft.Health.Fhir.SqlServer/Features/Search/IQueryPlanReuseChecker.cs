// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    public interface IQueryPlanReuseChecker
    {
        bool CanReuseQueryPlan(SearchOptions searchOptions);
    }
}
