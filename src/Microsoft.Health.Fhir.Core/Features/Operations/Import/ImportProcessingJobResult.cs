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
        /// Wall-clock time spent executing this import processing job, in milliseconds.
        /// Always populated for internal telemetry. Exposed in API responses only for in-memory test imports
        /// (see <see cref="ImportOrchestratorJobDefinition.InMemoryTestProcessingJobs"/>).
        /// </summary>
        public long ClockMilliseconds { get; set; }

        /// <summary>
        /// Wall-clock time spent in database calls during import processing, in milliseconds.
        /// Null when any database call in this job needed a retry, since retries make the measured duration unreliable.
        /// Always populated for internal telemetry. Exposed in API responses only for in-memory test imports
        /// (see <see cref="ImportOrchestratorJobDefinition.InMemoryTestProcessingJobs"/>).
        /// </summary>
        public long? DatabaseMilliseconds { get; set; }
    }
}
