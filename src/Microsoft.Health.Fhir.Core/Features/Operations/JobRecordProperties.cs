// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    /// <summary>
    /// Class for keeping track of the property names of the metadata for each job/operation.
    /// Some of these will be common across different operations and others might be specific.
    /// </summary>
    public static class JobRecordProperties
    {
        public const string JobRecord = "jobRecord";

        public const string Id = "id";

        public const string SecretName = "secretName";

        public const string Hash = "hash";

        public const string Status = "status";

        public const string LastModified = "lastModified";

        public const string QueuedTime = "queuedTime";

        public const string StartTime = "startTime";

        public const string EndTime = "endTime";

        public const string CanceledTime = "canceledTime";

        public const string RequestUri = "requestUri";

        public const string RequestorClaims = "requestorClaims";

        public const string ResourceType = "resourceType";

        public const string Progress = "progress";

        public const string Output = "output";

        public const string Query = "query";

        public const string Page = "page";

        public const string Error = "error";

        public const string Type = "type";

        public const string Url = "url";

        public const string Sequence = "sequence";

        public const string Count = "count";

        public const string CommitedBytes = "committedBytes";

        public const string SchemaVersion = "schemaVersion";
    }
}
