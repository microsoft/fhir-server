// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class ImportTaskConfiguration
    {
        /// <summary>
        /// Determines whether bulk import is enabled or not.
        /// </summary>
        public bool Enabled { get; set; } = true;

        public string ProcessingTaskQueueId { get; set; }

        /// <summary>
        /// Controls how many resources will be returned for each search query while importing the data.
        /// </summary>
        public uint MaximumConcurrency { get; set; } = 5;
    }
}
