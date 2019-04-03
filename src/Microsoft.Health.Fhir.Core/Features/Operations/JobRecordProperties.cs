// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    /// <summary>
    /// Class for keeping track of the different json properties that will be stored
    /// for each job record. Some of these will be common across different operations
    /// and others might be specific.
    /// </summary>
    public static class JobRecordProperties
    {
        public const string PartitonKey = "partitionKey";

        public const string Id = "id";

        public const string JobHash = "jobHash";

        public const string JobStatus = "jobStatus";

        public const string LastModified = "lastModified";

        public const string JobQueuedTime = "jobQueuedTime";

        public const string JobStartTime = "jobStartTime";

        public const string JobEndTime = "jobEndTime";

        public const string JobCancelledTime = "jobCancelledTime";

        public const string NumberOfConsecutiveFailures = "numberOfConsecutiveFailures";

        public const string TotalNumberOfFailures = "totalNumberOfFailures";

        public const string Request = "request";

        public const string Progress = "progress";

        public const string Output = "output";

        public const string Query = "query";

        public const string Page = "page";

        public const string Error = "error";

        public const string Result = "result";

        public const string Type = "type";

        public const string FileUri = "fileUri";

        public const string Sequence = "sequence";

        public const string Count = "count";

        public const string CommitedBytes = "committedBytes";

        public const string JobSchemaVersion = "jobSchemaVersion";
    }
}
