// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public class SearchParameterStatusManager : IRequireInitializationOnFirstRequest
    {
        private readonly ISearchParameterRegistry _searchParameterRegistry;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver;
        private readonly IMediator _mediator;

        public SearchParameterStatusManager(
            ISearchParameterRegistry searchParameterRegistry,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ISearchParameterSupportResolver searchParameterSupportResolver,
            IMediator mediator)
        {
            EnsureArg.IsNotNull(searchParameterRegistry, nameof(searchParameterRegistry));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(searchParameterSupportResolver, nameof(searchParameterSupportResolver));
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            _searchParameterRegistry = searchParameterRegistry;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _searchParameterSupportResolver = searchParameterSupportResolver;
            _mediator = mediator;
        }

        public async Task EnsureInitialized()
        {
            var updated = new List<SearchParameterInfo>();

            var parameters = (await _searchParameterRegistry.GetSearchParameterStatuses())
                .ToDictionary(x => x.Uri);

            // Set states of known parameters
            foreach (var p in _searchParameterDefinitionManager.AllSearchParameters)
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
            }

            await _mediator.Publish(new SearchParametersUpdated(updated));
        }
    }
}
