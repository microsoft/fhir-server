// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Configuration settings for SqlQueueClient.
    /// </summary>
    public class SqlQueueClientConfiguration
    {
        /// <summary>
        /// Section name for configuration
        /// </summary>
        public const string SectionName = "SqlQueueClient";

        /// <summary>
        /// Gets or sets the timeout for the commands executed by SqlQueueClient
        /// </summary>
        public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets the timeout for the dequeue operation
        /// </summary>
        public TimeSpan DequeueTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets the timeout for the enqueue operation
        /// </summary>
        public TimeSpan EnqueueTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets the timeout for completing a job
        /// </summary>
        public TimeSpan CompleteJobTimeout { get; set; } = TimeSpan.FromMinutes(5);
    }
}
