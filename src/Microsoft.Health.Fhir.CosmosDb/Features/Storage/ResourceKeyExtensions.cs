// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public static class ResourceKeyExtensions
    {
        public static string ToPartitionKey(this ResourceKey key)
        {
            return $"{key.ResourceType}_{key.Id}";
        }
    }
}
