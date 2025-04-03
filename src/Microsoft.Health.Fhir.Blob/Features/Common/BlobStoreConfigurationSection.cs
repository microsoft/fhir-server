// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Blob.Features.Common;

internal class BlobStoreConfigurationSection : IStoreConfigurationSection
{
    public BlobStoreConfigurationSection()
    {
        ConfigurationSectionName = BlobConstants.BlobStoreConfigurationSection;
        ContainerConfigurationName = BlobConstants.BlobContainerConfigurationName;
    }

    public override string ContainerConfigurationName { get; }

    public override string ConfigurationSectionName { get; }
}
