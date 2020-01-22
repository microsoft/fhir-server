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
        /// Determines the type of the default storage account that will be used to export data to.
        /// </summary>
        public string DefaultStorageAccountType { get; set; }

        /// <summary>
        /// Determines the default storage account that will be used to export data to.
        /// </summary>
        public string DefaultStorageAccountConnection { get; set; }

        /// <summary>
        /// Determines the default access token provider type that will be used to get an access token to
        /// the storage account used for exporting data.
        /// </summary>
        public string AccessTokenProviderType { get; set; }

        public ushort MaximumNumberOfConcurrentJobsAllowed { get; set; } = 1;

        public TimeSpan JobHeartbeatTimeoutThreshold { get; set; } = TimeSpan.FromMinutes(10);

        public TimeSpan JobPollingFrequency { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// List of destinations that are supported for export operation.
        /// </summary>
        public HashSet<string> SupportedDestinations { get; } = new HashSet<string>(StringComparer.Ordinal);

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
