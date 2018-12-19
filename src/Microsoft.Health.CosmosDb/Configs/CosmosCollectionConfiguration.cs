// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.CosmosDb.Configs
{
    public class CosmosCollectionConfiguration
    {
        public string CollectionId { get; set; }

        public int? InitialCollectionThroughput { get; set; }
    }
}
