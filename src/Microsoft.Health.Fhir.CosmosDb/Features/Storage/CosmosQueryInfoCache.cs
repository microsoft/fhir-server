// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class CosmosQueryInfoCache
    {
        private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 512 });

        public bool IsQueryKnownToBeSelective(string queryText) => _cache.TryGetValue(queryText, out _);

        public void SetQueryKnownToBeSelective(string queryText) => _cache.Set(queryText, true);
    }
}
