// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models
{
    public class ContainerRegistryInfo
    {
        public string ContainerRegistryServer { get; set; } = string.Empty;

        public string ContainerRegistryUsername { get; set; } = string.Empty;

        public string ContainerRegistryPassword { get; set; } = string.Empty;
    }
}
