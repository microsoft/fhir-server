// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Blob.Features.Common;

/// <summary>
/// This class is used to pass the named blob container configuration section to the blob health check.
/// </summary>
public class BlobStoreConfigurationSection
{
    public BlobStoreConfigurationSection()
    {
        ConfigurationSectionName = BlobConstants.BlobStoreConfigurationSection;
        ContainerConfigurationName = BlobConstants.BlobContainerConfigurationName;
    }

    public string ContainerConfigurationName { get; }

    public string ConfigurationSectionName { get; }
}
