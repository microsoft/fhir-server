// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Blob.Features.Common;

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
