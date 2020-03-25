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
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public class SearchParameterStatusManager : IRequireInitializationOnFirstRequest
    {
        private readonly ISearchParameterRegistry _searchParameterRegistry;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly IMediator _mediator;

        public SearchParameterStatusManager(
            ISearchParameterRegistry searchParameterRegistry,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IMediator mediator)
        {
            EnsureArg.IsNotNull(searchParameterRegistry, nameof(searchParameterRegistry));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            _searchParameterRegistry = searchParameterRegistry;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _mediator = mediator;
        }

        public async Task EnsureInitialized()
        {
            var updated = new List<SearchParameterInfo>();
            var newParameters = new List<ResourceSearchParameterStatus>();

            var parameters = (await _searchParameterRegistry.GetSearchParameterStatuses())
                .ToDictionary(x => x.Uri);

            // Set states of known parameters
            foreach (var p in _searchParameterDefinitionManager.AllSearchParameters)
            {
                if (parameters.TryGetValue(p.Url, out var result))
                {
                    p.IsSearchable = result.Status == SearchParameterStatus.Enabled;
                    p.IsSupported = result.Status != SearchParameterStatus.Disabled;
                    p.IsPartiallySupported = result.IsPartiallySupported;
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
                    p.IsSupported = true;
                }

                updated.Add(p);
            }

            if (newParameters.Any())
            {
                await _searchParameterRegistry.UpdateStatuses(newParameters);
            }

            await _mediator.Publish(new SearchParametersUpdated(updated));
        }
    }
}
