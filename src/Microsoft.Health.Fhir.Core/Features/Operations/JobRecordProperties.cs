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

        public const string QueryList = "queryList";

        public const string QueryListErrors = "queryListErrors";

        public const string Page = "page";

        public const string Error = "error";

        public const string Type = "type";

        public const string Url = "url";

        public const string Sequence = "sequence";

        public const string ResourceCounts = "resourceCounts";

        public const string Count = "count";

        public const string CommitedBytes = "committedBytes";

        public const string SchemaVersion = "schemaVersion";

        public const string FailureReason = "failureReason";

        public const string FailureStatusCode = "failureStatusCode";

        public const string FailureDetails = "failureDetails";

        public const string Since = "since";

        public const string FailureCount = "failureCount";

        public const string EndResourceSurrogateId = "endResourceSurrogateId";

        public const string StorageAccountConnectionHash = "storageAccountConnectionHash";

        public const string StorageAccountUri = "storageAccountUri";

        public const string MaximumConcurrency = "maximumConcurrency";

        public const string MaximumNumberOfResourcesPerQuery = "maximumNumberOfResourcesPerQuery";

        public const string NumberOfPagesPerCommit = "numberOfPagesPerCommit";

        public const string SubSearch = "subSearch";

        public const string TriggeringResourceId = "triggeringResourceId";

        public const string ExportType = "exportType";

        public const string Resources = "resources";

        public const string SearchParams = "searchParams";

        public const string AnonymizationConfigurationCollectionReference = "anonymizationConfigurationCollectionReference";

        public const string AnonymizationConfigurationLocation = "anonymizationConfigurationLocation";

        public const string AnonymizationConfigurationFileETag = "anonymizationConfigurationFileHash";

        public const string ContinuationToken = "continuationToken";

        public const string ResourceReindexProgressByResource = "resourceReindexProgressByResource";

        public const string GroupId = "groupId";

        public const string StorageAccountContainerName = "storageAccountContainerName";

        public const string Filters = "filters";

        public const string CurrentFilter = "currentFilter";

        public const string FilteredSearchesComplete = "FilteredSearchesComplete";

        public const string ResourceTypeSearchParameterHashMap = "resourceTypeSearchParameterHashMap";

        public const string ExportFormat = "exportFormat";

        public const string RollingFileSizeInMB = "rollingFileSizeInMB";

        public const string Issues = "issues";

        public const string TotalResourcesToReindex = "totalResourcesToReindex";

        public const string ResourcesSuccessfullyReindexed = "resourcesSuccessfullyReindexed";

        public const string QueryDelayIntervalInMilliseconds = "queryDelayIntervalInMilliseconds";

        public const string TargetDataStoreUsagePercentage = "targetDataStoreUsagePercentage";

        public const string TargetResourceTypes = "targetResourceTypes";

        public const string TargetSearchParameterTypes = "targetSearchParameterTypes";

        public const string SearchParameterResourceTypes = "searchParameterResourceTypes";

        public const string CreatedChild = "createdChild";

        public const string Till = "till";

        public const string StartSurrogateId = "startSurrogateId";

        public const string EndSurrogateId = "endSurrogateId";

        public const string MaxResourceSurrogateId = "maxResourceSurrogateId";

        public const string GlobalStartSurrogateId = "globalStartSurrogateId";

        public const string GlobalEndSurrogateId = "globalEndSurrogateId";

        public const string RestartCount = "restartCount";

        public const string TypeId = "typeId";

        public const string IsParallel = "isParallel";

        public const string IncludeHistory = "includeHistory";

        public const string IncludeDeleted = "includeDeleted";

        public const string SmartRequest = "smartRequest";

        public const string DeleteOperation = "deleteOperation";

        public const string SearchParameters = "searchParameters";

        public const string ResourcesDeleted = "resourcesDeleted";

        public const string BaseUrl = "baseUrl";

        public const string ParentRequestId = "parentRequestId";

        public const string ExpectedResourceCount = "expectedResourceCount";

        public const string VersionType = "versionType";
    }
}
