// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Blob.Features.Common;

internal class BlobStoreConfigurationSection : IStoreConfigurationSection
{
    internal BlobStoreConfigurationSection()
    {
        ConfigurationSectionName = BlobConstants.BlobStoreConfigurationSection;
        ContainerConfigurationName = BlobConstants.BlobContainerConfigurationName;
    }

    public new string ContainerConfigurationName { get; }

    public new string ConfigurationSectionName { get; }

    public string ContainerName { get; set; }
}
