// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.CosmosDb.Configs
{
    public class CosmosDataStoreRetryOptions
    {
        /// <summary>
        /// Gets the maximum number of retries allowed.
        /// </summary>
        public int MaxNumberOfRetries { get; set; } = 3;

        /// <summary>
        /// Gets the maximum number of seconds to wait while the retries are happening.
        /// </summary>
        public int MaxWaitTimeInSeconds { get; set; } = 5;
    }
}
