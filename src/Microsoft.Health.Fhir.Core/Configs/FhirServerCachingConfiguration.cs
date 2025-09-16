// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class FhirServerCachingConfiguration
    {
        public const string SectionName = "FhirServer:Caching";

        /// <summary>
        /// Redis caching configuration
        /// </summary>
        public RedisConfiguration Redis { get; set; } = new RedisConfiguration();
    }
}
