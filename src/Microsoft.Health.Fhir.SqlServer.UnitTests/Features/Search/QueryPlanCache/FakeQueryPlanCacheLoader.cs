// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Search.QueryPlanCache;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.QueryPlanCache
{
    public sealed class FakeQueryPlanCacheLoader : IQueryPlanCacheLoader
    {
        private readonly bool _isEnabled;

        public FakeQueryPlanCacheLoader(bool isEnabled)
        {
            _isEnabled = isEnabled;
        }

        public bool IsEnabled()
        {
            return _isEnabled;
        }

        public void Reset()
        {
        }
    }
}
