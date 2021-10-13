// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Import.Core;
using Microsoft.Health.Fhir.Import.Core.Test;

namespace Microsoft.Health.Fhir.Import.DataStore.CosmosDb
{
    public class CosmosDbResourceMetaPopulator : IResourceMetaPopulator
    {
        public void Populate(long id, Resource resource)
        {
            if (resource.Meta == null)
            {
                resource.Meta = new Meta();
            }

            resource.Meta.LastUpdated = ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(id);
        }
    }
}
