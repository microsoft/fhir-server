// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportProcessingJobResult
    {
        /// <summary>
        /// Succeeded imported resource count
        /// </summary>
        public long SucceededResources { get; set; }

        /// <summary>
        /// Failed processing resource count
        /// </summary>
        public long FailedResources { get; set; }

        /// <summary>
        /// Processed bytes from blob/file
        /// </summary>
        public long ProcessedBytes { get; set; }

        /// <summary>
        /// If any failure processing resource, error log would be uploaded.
        /// </summary>
        public string ErrorLogLocation { get; set; }

        /// <summary>
        /// Critical error during data processing.
        /// </summary>
        public string ErrorDetails { get; set; }

        /// <summary>
        /// Wall-clock time to retrieve existing resources from the data store in milliseconds.
        /// Always populated for internal telemetry. Exposed in API responses only when IntegrationDataStore:EnableTestSourceOverride is enabled.
        /// </summary>
        public long GetResourcesMilliseconds { get; set; }

        /// <summary>
        /// Wall-clock time to merge (persist) resources to the data store in milliseconds.
        /// Always populated for internal telemetry. Exposed in API responses only when IntegrationDataStore:EnableTestSourceOverride is enabled.
        /// </summary>
        public long MergeResourcesMilliseconds { get; set; }

        /// <summary>
        /// Number of resource retrieval calls made to the data store.
        /// </summary>
        public long GetResourcesCallCount { get; set; }

        /// <summary>
        /// Number of resource merge calls made to the data store.
        /// </summary>
        public long MergeResourcesCallCount { get; set; }
    }
}
