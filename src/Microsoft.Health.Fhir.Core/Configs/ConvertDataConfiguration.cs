// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.TemplateManagement.Models;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class ConvertDataConfiguration
    {
        /// <summary>
        /// Determines whether convertData is enabled or not.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Determines the registered container registry list.
        /// </summary>
        public List<string> ContainerRegistryServers { get; } = new List<string>();

        /// <summary>
        /// Configuration for templates.
        /// </summary>
        public TemplateCollectionConfiguration TemplateCollectionOptions { get; set; } = new TemplateCollectionConfiguration();

        /// <summary>
        /// Cache size limit for data convert, cache entries includes registry tokens, image manifests, image layer blobs.
        /// Size of each cache entry are calculated by byte counts, i.e. length of a token, number of bytes of a manifest or a blob.
        /// </summary>
        public long CacheSizeLimit { get; set; } = 100_000_000;

        /// <summary>
        /// Determines timeout for convert execution to terminate long running templates.
        /// </summary>
        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }
}
