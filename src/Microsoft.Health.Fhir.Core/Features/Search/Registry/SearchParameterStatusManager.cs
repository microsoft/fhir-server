// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Core;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public class SearchParameterStatusManager : IRequireInitializationOnFirstRequest
    {
        private readonly ISearchParameterStatusDataStore _searchParameterStatusDataStore;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver;
        private readonly IMediator _mediator;

        public SearchParameterStatusManager(
            ISearchParameterStatusDataStore searchParameterStatusDataStore,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ISearchParameterSupportResolver searchParameterSupportResolver,
            IMediator mediator)
        {
            EnsureArg.IsNotNull(searchParameterStatusDataStore, nameof(searchParameterStatusDataStore));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(searchParameterSupportResolver, nameof(searchParameterSupportResolver));
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            _searchParameterStatusDataStore = searchParameterStatusDataStore;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _searchParameterSupportResolver = searchParameterSupportResolver;
            _mediator = mediator;
        }

        public async Task EnsureInitialized()
        {
            var updated = new List<SearchParameterInfo>();
            var resourceTypeSearchParamStatusMap = new Dictionary<string, List<ResourceSearchParameterStatus>>();

            var parameters = (await _searchParameterStatusDataStore.GetSearchParameterStatuses())
                .ToDictionary(x => x.Uri);

            // Set states of known parameters
            foreach (SearchParameterInfo p in _searchParameterDefinitionManager.AllSearchParameters)
            {
                if (parameters.TryGetValue(p.Url, out ResourceSearchParameterStatus result))
                {
                    bool isSearchable = result.Status == SearchParameterStatus.Enabled;
                    bool isSupported = result.Status != SearchParameterStatus.Disabled;
                    bool isPartiallySupported = result.IsPartiallySupported;

                    if (result.Status == SearchParameterStatus.Disabled)
                    {
                        // Re-check if this parameter is now supported.
                        (bool Supported, bool IsPartiallySupported) supportedResult = _searchParameterSupportResolver.IsSearchParameterSupported(p);
                        isSupported = supportedResult.Supported;
                        isPartiallySupported = supportedResult.IsPartiallySupported;
                    }

                    if (p.IsSearchable != isSearchable ||
                        p.IsSupported != isSupported ||
                        p.IsPartiallySupported != isPartiallySupported)
                    {
                        p.IsSearchable = isSearchable;
                        p.IsSupported = isSupported;
                        p.IsPartiallySupported = isPartiallySupported;

                        updated.Add(p);
                    }
                }
                else
                {
                    p.IsSearchable = false;

                    // Check if this parameter is now supported.
                    (bool Supported, bool IsPartiallySupported) supportedResult = _searchParameterSupportResolver.IsSearchParameterSupported(p);
                    p.IsSupported = supportedResult.Supported;
                    p.IsPartiallySupported = supportedResult.IsPartiallySupported;

                    updated.Add(p);
                }

                // We need to keep track of supported or partially supported parameters.
                // These parameters will be used to calculate the search parameter hash below.
                if (p.IsPartiallySupported || p.IsSupported)
                {
                    if (result == null)
                    {
                        result = new ResourceSearchParameterStatus()
                        {
                            Uri = p.Url,
                            Status = SearchParameterStatus.Supported,
                            LastUpdated = Clock.UtcNow,
                        };
                    }

                    if (p.TargetResourceTypes != null)
                    {
                        foreach (string resourceType in p.TargetResourceTypes)
                        {
                            if (resourceTypeSearchParamStatusMap.ContainsKey(resourceType))
                            {
                                resourceTypeSearchParamStatusMap[resourceType].Add(result);
                            }
                            else
                            {
                                resourceTypeSearchParamStatusMap.Add(resourceType, new List<ResourceSearchParameterStatus>() { result });
                            }
                        }
                    }

                    if (p.BaseResourceTypes != null)
                    {
                        foreach (string resourceType in p.BaseResourceTypes)
                        {
                            if (resourceTypeSearchParamStatusMap.ContainsKey(resourceType))
                            {
                                resourceTypeSearchParamStatusMap[resourceType].Add(result);
                            }
                            else
                            {
                                resourceTypeSearchParamStatusMap.Add(resourceType, new List<ResourceSearchParameterStatus>() { result });
                            }
                        }
                    }
                }
            }

            var resourceHashMap = new Dictionary<string, string>();
            foreach (KeyValuePair<string, List<ResourceSearchParameterStatus>> kvp in resourceTypeSearchParamStatusMap)
            {
                string searchParamHash = SearchHelperUtilities.CalculateSearchParameterHash(kvp.Value);
                resourceHashMap.Add(kvp.Key, searchParamHash);
            }

            await _mediator.Publish(new SearchParametersHashUpdated(resourceHashMap));

            await _mediator.Publish(new SearchParametersUpdated(updated));
        }
    }
}
