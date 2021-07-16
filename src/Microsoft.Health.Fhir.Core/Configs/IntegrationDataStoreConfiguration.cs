// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class IntegrationDataStoreConfiguration
    {
        private const int DefaultMaxRetryCount = 3;

        private const int DefaultRetryInternalInSeconds = 5;

        private const int DefaultMaxWaitTimeInSeconds = -1;

        public string StorageAccountConnection { get; set; } = string.Empty;

        /// <summary>
        /// Determines the storage account connection that will be used to integration data store to.
        /// Should be a uri pointing to the required storage account.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "CA1056:Uri properties should not be strings",
            Justification = "Set from an environment variable.")]
        public string StorageAccountUri { get; set; } = string.Empty;

        public int MaxRetryCount { get; set; } = DefaultMaxRetryCount;

        public int RetryInternalInSecondes { get; set; } = DefaultRetryInternalInSeconds;

        public int MaxWaitTimeInSeconds { get; set; } = DefaultMaxWaitTimeInSeconds;
    }
}
