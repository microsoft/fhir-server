// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Caching;
using Microsoft.Health.Fhir.Core.Features.Caching.Redis;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.Core.Features.Search.Caching
{
    /// <summary>
    /// Redis-based implementation for search parameter status caching
    /// </summary>
    public class RedisSearchParameterCache : RedisDistributedCache<ResourceSearchParameterStatus>, ISearchParameterCache
    {
        public RedisSearchParameterCache(
            Microsoft.Extensions.Caching.Distributed.IDistributedCache distributedCache,
            CacheTypeConfiguration configuration,
            ILogger<RedisDistributedCache<ResourceSearchParameterStatus>> logger,
            ISearchParameterStatusDataStore dataStore)
            : base(distributedCache, configuration, logger, dataStore)
        {
        }
    }
}
