// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class ValidateOperationConfiguration
    {
        public const int DefaultCacheDurationInSeconds = 14400;
        public const int DefaultMaxExpansionSize = 20000;

        /// <summary>
        /// For how long we cache profile in memory.
        /// </summary>
        public int CacheDurationInSeconds { get; set; } = DefaultCacheDurationInSeconds;

        /// <summary>
        /// Max number of codes in a ValueSet.
        /// </summary>
        public int MaxExpansionSize { get; set; } = DefaultMaxExpansionSize;
    }
}
