// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        private readonly Func<IScoped<ISearchParameterRegistryDataStore>> _searchParameterRegistryFactory;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver;
        private readonly IMediator _mediator;

        public SearchParameterStatusManager(
            Func<IScoped<ISearchParameterRegistryDataStore>> searchParameterRegistryFactory,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ISearchParameterSupportResolver searchParameterSupportResolver,
            IMediator mediator)
        {
            EnsureArg.IsNotNull(searchParameterRegistryFactory, nameof(searchParameterRegistryFactory));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(searchParameterSupportResolver, nameof(searchParameterSupportResolver));
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            _searchParameterRegistryFactory = searchParameterRegistryFactory;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _searchParameterSupportResolver = searchParameterSupportResolver;
            _mediator = mediator;
        }

        public async Task EnsureInitialized()
        {
            var updated = new List<SearchParameterInfo>();
            var newParameters = new List<ResourceSearchParameterStatus>();

            Dictionary<Uri, ResourceSearchParameterStatus> parameters;

            using (IScoped<ISearchParameterRegistryDataStore> registry = _searchParameterRegistryFactory.Invoke())
            {
                parameters = (await registry.Value.GetSearchParameterStatuses()).ToDictionary(x => x.Uri);
            }

            // Set states of known parameters
            foreach (var p in _searchParameterDefinitionManager.AllSearchParameters)
            {
                if (parameters.TryGetValue(p.Url, out ResourceSearchParameterStatus result))
                {
                    bool isSearchable = result.Status == SearchParameterStatus.Enabled;
                    bool isSupported = result.Status != SearchParameterStatus.Disabled;

                    if (result.Status == SearchParameterStatus.Disabled)
                    {
                        // Re-check if this parameter is now supported.
                        isSupported = _searchParameterSupportResolver.IsSearchParameterSupported(p);
                    }

                    if (p.IsSearchable != isSearchable ||
                        p.IsSupported != isSupported ||
                        p.IsPartiallySupported != result.IsPartiallySupported)
                    {
                        p.IsSearchable = isSearchable;
                        p.IsSupported = isSupported;
                        p.IsPartiallySupported = result.IsPartiallySupported;

                        updated.Add(p);
                    }
                }
                else
                {
                    newParameters.Add(new ResourceSearchParameterStatus
                    {
                        Uri = p.Url,
                        LastUpdated = Clock.UtcNow,
                        Status = SearchParameterStatus.Supported,
                    });

                    p.IsSearchable = false;

                    // Check if this parameter is now supported.
                    p.IsSupported = _searchParameterSupportResolver.IsSearchParameterSupported(p);

                    updated.Add(p);
                }
            }

            if (newParameters.Any())
            {
                using (IScoped<ISearchParameterRegistryDataStore> registry = _searchParameterRegistryFactory.Invoke())
                {
                    await registry.Value.UpsertStatuses(newParameters);
                }
            }

            await _mediator.Publish(new SearchParametersUpdated(updated));
        }
    }
}
