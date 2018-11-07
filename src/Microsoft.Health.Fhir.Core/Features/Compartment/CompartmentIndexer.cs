// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
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

        public CompartmentIndices Extract(ResourceType resourceType, IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            EnsureArg.IsNotNull(searchIndices, nameof(searchIndices));
            var compartmentTypeToResourceIds = new Dictionary<CompartmentType, IReadOnlyCollection<string>>();
            Dictionary<CompartmentType, List<SearchIndexEntry>> searchIndicesByCompartmentType = ExtractSearchIndexByCompartmentType(searchIndices);

            foreach (CompartmentType compartmentType in Enum.GetValues(typeof(CompartmentType)))
            {
                compartmentTypeToResourceIds[compartmentType] = null;

                if (_compartmentDefinitionManager.TryGetSearchParams(resourceType, compartmentType, out HashSet<string> searchParams) && searchIndicesByCompartmentType.TryGetValue(compartmentType, out List<SearchIndexEntry> searchIndicesForCompartment))
                {
                    var searchEntries = searchIndicesForCompartment.Where(si => searchParams.Contains(si.ParamName));

                    var resourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                        compartmentTypeToResourceIds[compartmentType] = resourceIds;
                    }
                }
            }

            var compartmentIndices = new CompartmentIndices(compartmentTypeToResourceIds);
            return compartmentIndices;
        }

        private static Dictionary<CompartmentType, List<SearchIndexEntry>> ExtractSearchIndexByCompartmentType(IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            var retDict = new Dictionary<CompartmentType, List<SearchIndexEntry>>();

            foreach (var indexEntry in searchIndices)
            {
                if (indexEntry.Value is ReferenceSearchValue refValue && CompartmentDefinitionManager.ResourceTypeToCompartmentType.TryGetValue(refValue.ResourceType.Value, out CompartmentType compartmentType))
                {
                    if (!retDict.TryGetValue(compartmentType, out List<SearchIndexEntry> searchIndexEntries))
                    {
                        retDict[compartmentType] = new List<SearchIndexEntry>();
                    }

                    retDict[compartmentType].Add(indexEntry);
                }
            }

            return retDict;
        }
    }
}
