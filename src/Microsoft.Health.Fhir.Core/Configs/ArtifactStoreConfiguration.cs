// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class ArtifactStoreConfiguration
    {
        /// <summary>
        /// Determines the registered artifact instances list.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a configuration class")]
        public IList<ArtifactStoreInstanceConfiguration> Instances { get; set; }
    }
}
