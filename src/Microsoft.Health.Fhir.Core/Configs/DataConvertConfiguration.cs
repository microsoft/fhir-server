// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class DataConvertConfiguration
    {
        /// <summary>
        /// Determines whether dataConvert is enabled or not.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Determines the registered container registry list.
        /// </summary>
        public List<ContainerRegistryInfo> ContainerRegistries { get; } = new List<ContainerRegistryInfo>();

        /// <summary>
        /// Determines the expiration duration of the container registry token.
        /// </summary>
        public TimeSpan ContainerRegistryTokenExpiration { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Determines timeout for convert execution to terminate long running templates.
        /// </summary>
        public TimeSpan ProcessTimeoutThreshold { get; set; } = TimeSpan.FromSeconds(30);
    }
}
