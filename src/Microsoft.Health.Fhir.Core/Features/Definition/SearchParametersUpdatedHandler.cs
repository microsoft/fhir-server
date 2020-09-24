// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.Reindex;
using Microsoft.Health.Fhir.Core.Messages.Search;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    public class SearchParametersUpdatedHandler : INotificationHandler<SearchParametersHashUpdated>, INotificationHandler<ReindexJobCompleted>
    {
        private SearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterRegistry _searchParameterRegistry;

        public SearchParametersUpdatedHandler(SearchParameterDefinitionManager searchParameterDefinitionManager, ISearchParameterRegistry searchParameterRegsitry)
        {
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));

            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _searchParameterRegistry = searchParameterRegsitry;
        }

        public Task Handle(SearchParametersHashUpdated notification, CancellationToken cancellationToken)
        {
            _searchParameterDefinitionManager.SearchParametersHash = notification.HashValue;
            return Task.CompletedTask;
        }

        public Task Handle(ReindexJobCompleted notification, CancellationToken cancellationToken)
        {
            var searchParameterStatusList = new List<ResourceSearchParameterStatus>();
            foreach (string uri in notification.SearchParameterUrls)
            {
                var searchParamUri = new Uri(uri);
                var searchParam = _searchParameterDefinitionManager.GetSearchParameter(searchParamUri);
                searchParam.IsSearchable = true;

                searchParameterStatusList.Add(new ResourceSearchParameterStatus()
                {
                    LastUpdated = DateTimeOffset.UtcNow,
                    Status = SearchParameterStatus.Enabled,
                    Uri = searchParamUri,
                });
            }

            return _searchParameterRegistry.UpdateStatuses(searchParameterStatusList);
        }
    }
}
