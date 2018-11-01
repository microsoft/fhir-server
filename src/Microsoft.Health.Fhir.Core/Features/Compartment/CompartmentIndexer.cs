// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Compartment
{
    public class CompartmentIndexer : ICompartmentIndexer
    {
        private readonly ICompartmentDefinitionManager _compartmentDefinitionManager;

        public CompartmentIndexer(ICompartmentDefinitionManager compartmentDefinitionManager)
        {
            EnsureArg.IsNotNull(compartmentDefinitionManager, nameof(compartmentDefinitionManager));
            _compartmentDefinitionManager = compartmentDefinitionManager;
        }

        public IReadOnlyCollection<string> Extract(ResourceType resourceType, CompartmentType compartmentType, IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            if (_compartmentDefinitionManager.TryGetSearchParams(resourceType, compartmentType, out HashSet<string> searchParams) && searchIndices != null)
            {
                var searchEntries = searchIndices.Where(si => searchParams.Contains(si.ParamName));

                var resourceIds = new HashSet<string>();
                ResourceType compartmentResourceType = CompartmentDefinitionManager.CompartmentTypeToResourceType(compartmentType);

                foreach (SearchIndexEntry entry in searchEntries)
                {
                    var refValue = (ReferenceSearchValue)entry.Value;
                    if (refValue.ResourceType == compartmentResourceType)
                    {
                        resourceIds.Add(refValue.ResourceId);
                    }
                }

                if (resourceIds.Any())
                {
                    return resourceIds;
                }
            }

            return null;
        }
    }
}
