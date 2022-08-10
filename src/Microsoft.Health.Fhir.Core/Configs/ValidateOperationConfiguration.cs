// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class ValidateOperationConfiguration
    {
        /// <summary>
        /// For how long we cache profile in memory.
        /// </summary>
        public int CacheDurationInSeconds { get; set; } = 14400;

        /// <summary>
        /// Url indicating an external terminology service that can be used for validation purposes
        /// </summary>
        public string ProfileValidationTerminologyServer { get; set; } = null;

        /// <summary>
        /// ApiKey for secure terminology servers
        /// </summary>
        public string ApiKey { get; set; } = null;
    }
}
