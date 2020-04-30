// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

﻿using System;
﻿using System.Collections.Generic;
﻿using System.Linq;
﻿using EnsureThat;
﻿using Microsoft.Health.Fhir.Core.Features.Compartment;
﻿using Microsoft.Health.Fhir.Core.Features.Persistence;
﻿using Microsoft.Health.Fhir.Core.Features.Search;
﻿using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
﻿using Microsoft.Health.Fhir.ValueSets;

﻿namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    public class CompartmentIndexer : ICompartmentIndexer
    {
        private readonly ICompartmentDefinitionManager _compartmentDefinitionManager;

        public CompartmentIndexer(ICompartmentDefinitionManager compartmentDefinitionManager)
        {
            EnsureArg.IsNotNull(compartmentDefinitionManager, nameof(compartmentDefinitionManager));
            _compartmentDefinitionManager = compartmentDefinitionManager;
        }

        public CompartmentIndices Extract(string resourceType, IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            EnsureArg.IsNotNull(searchIndices, nameof(searchIndices));

            var compartmentTypeToResourceIds = new Dictionary<string, IReadOnlyCollection<string>>();
            Dictionary<string, List<SearchIndexEntry>> searchIndicesByCompartmentType = ExtractSearchIndexByCompartmentType(searchIndices);

            foreach (CompartmentType compartmentType in Enum.GetValues(typeof(CompartmentType)))
            {
                string compartmentTypeLiteral = compartmentType.ToString();
                compartmentTypeToResourceIds[compartmentTypeLiteral] = null;

                if (_compartmentDefinitionManager.TryGetSearchParams(resourceType, compartmentType, out HashSet<string> searchParams) && searchIndicesByCompartmentType.TryGetValue(compartmentTypeLiteral, out List<SearchIndexEntry> searchIndicesForCompartment))
                {
                    var searchEntries = searchIndicesForCompartment.Where(si => searchParams.Contains(si.SearchParameter.Name));

                    var resourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string compartmentResourceType = CompartmentDefinitionManager.CompartmentTypeToResourceType(compartmentTypeLiteral);

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
                        compartmentTypeToResourceIds[compartmentType.ToString()] = resourceIds;
                    }
                }
            }

            var compartmentIndices = new CompartmentIndices(compartmentTypeToResourceIds);
            return compartmentIndices;
        }

        private static Dictionary<string, List<SearchIndexEntry>> ExtractSearchIndexByCompartmentType(IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            var retDict = new Dictionary<string, List<SearchIndexEntry>>();

            foreach (var indexEntry in searchIndices)
            {
                if (indexEntry.Value is ReferenceSearchValue refValue && !string.IsNullOrWhiteSpace(refValue.ResourceType) && CompartmentDefinitionManager.ResourceTypeToCompartmentType.TryGetValue(refValue.ResourceType, out CompartmentType compartmentType))
                {
                    string key = compartmentType.ToString();

                    if (!retDict.TryGetValue(key, out List<SearchIndexEntry> searchIndexEntries))
                    {
                        searchIndexEntries = new List<SearchIndexEntry>();
                        retDict[key] = searchIndexEntries;
                    }

                    searchIndexEntries.Add(indexEntry);
                }
            }

            return retDict;
        }
    }
}
