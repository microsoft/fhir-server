// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// Populate resource with meta content.
    /// </summary>
    public interface IResourceMetaPopulator
    {
        /// <summary>
        /// Populate meta content.
        /// </summary>
        /// <param name="id">sequence id of the resource.</param>
        /// <param name="resource">resource.</param>
        public void Populate(long id, Resource resource);
    }
}
