// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    internal class SqlResourceMetaPopulator : IResourceMetaPopulator
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
