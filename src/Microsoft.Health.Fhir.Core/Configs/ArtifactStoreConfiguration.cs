// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.ArtifactStore;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class ArtifactStoreConfiguration
    {
        public ICollection<OciArtifactInfo> OciArtifacts { get; } = new List<OciArtifactInfo>();
    }
}
