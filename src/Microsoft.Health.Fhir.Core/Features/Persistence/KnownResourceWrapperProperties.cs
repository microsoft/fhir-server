// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public static class KnownResourceWrapperProperties
    {
        public const string LastModified = "lastModified";

        public const string RawResource = "rawResource";

        public const string IsDeleted = "isDeleted";

        public const string IsHistory = "isHistory";

        public const string ResourceId = "resourceId";

        public const string ResourceTypeName = "resourceTypeName";

        public const string Request = "request";

        public const string PartitionKey = "partitionKey";

        public const string Version = "version";

        public const string DataVersion = "dataVersion";

        public const string SearchIndices = "searchIndices";

        public const string IsSystem = "isSystem";

        public const string LastModifiedClaims = "lastModifiedClaims";
    }
}
