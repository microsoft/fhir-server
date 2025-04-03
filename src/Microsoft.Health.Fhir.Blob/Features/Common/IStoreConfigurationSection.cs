// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Blob.Features.Common;

public class IStoreConfigurationSection
{
    public virtual string ConfigurationSectionName { get; }

    public virtual string ContainerConfigurationName { get; }
}
