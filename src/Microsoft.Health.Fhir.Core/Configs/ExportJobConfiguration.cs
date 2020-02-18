// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class ExportJobConfiguration
    {
        /// <summary>
        /// Determines whether export is enabled or not.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Determines the storage account connection that will be used to export data to.
        /// Should be a connection string to the required storage account.
        /// </summary>
        public string StorageAccountConnection { get; set; }

        /// <summary>
        /// Determines the storage account connection that will be used to export data to.
        /// Should be a uri pointing to the required storage account.
        /// </summary>
#pragma warning disable CA1056 // Uri properties should not be strings
        public string StorageAccountUri { get; set; }
#pragma warning restore CA1056 // Uri properties should not be strings

        public ushort MaximumNumberOfConcurrentJobsAllowed { get; set; } = 1;

        public TimeSpan JobHeartbeatTimeoutThreshold { get; set; } = TimeSpan.FromMinutes(10);

        public TimeSpan JobPollingFrequency { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Controls how many resources will be returned for each search query while exporting the data.
        /// </summary>
        public uint MaximumNumberOfResourcesPerQuery { get; set; } = 100;

        /// <summary>
        /// Number of pages to be iterated before committing the export progress.
        /// </summary>
        public uint NumberOfPagesPerCommit { get; set; } = 10;
    }
}
