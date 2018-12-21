// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public static class KnownDocumentProperties
    {
        public const string ETag = "_etag";

        public const string Id = "id";

        public const string IsSystem = "isSystem";

        public const string PartitionKey = "partitionKey";

        public const string SelfLink = "_self";

        public const string Timestamp = "_ts";
    }
}
