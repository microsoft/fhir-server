// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

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
        public string StorageAccountConnection { get; set; } = string.Empty;

        /// <summary>
        /// Determines the storage account connection that will be used to export data to.
        /// Should be a uri pointing to the required storage account.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "CA1056:Uri properties should not be strings",
            Justification = "Set from an environment variable.")]
        public string StorageAccountUri { get; set; } = string.Empty;

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

        /// <summary>
        /// Formats for export jobs.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a configuration class")]
        public IList<ExportJobFormatConfiguration> Formats { get; set; }
    }
}
