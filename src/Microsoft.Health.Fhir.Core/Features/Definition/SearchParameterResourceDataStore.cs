// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public class SearchParameterResourceDataStore : IRequireInitializationOnFirstRequest
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly IModelInfoProvider _modelInfoProvider;

        public SearchParameterResourceDataStore(
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            SearchParameterStatusManager searchParameterStatusManager,
            Func<IScoped<ISearchService>> searchServiceFactory,
            IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _searchParameterStatusManager = searchParameterStatusManager;
            _searchServiceFactory = searchServiceFactory;
            _modelInfoProvider = modelInfoProvider;
        }

        public async Task EnsureInitialized()
        {
            // now read in any previously POST'd SearchParameter resources
            using (var search = _searchServiceFactory())
            {
                string continuationToken = null;
                do
                {
                    var queryParams = new List<Tuple<string, string>>();
                    if (continuationToken != null)
                    {
                        queryParams.Add(new Tuple<string, string>(KnownQueryParameterNames.ContinuationToken, continuationToken));
                    }

                    var result = await search.Value.SearchAsync("SearchParameter", queryParams, cancellationToken: default);
                    if (!string.IsNullOrEmpty(result?.ContinuationToken))
                    {
                        continuationToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(result.ContinuationToken));
                    }
                    else
                    {
                        continuationToken = null;
                    }

                    if (result != null && result.Results != null && result.Results.Count() > 0)
                    {
                        var searchParams = result.Results.Select(r => r.Resource.RawResource.ToITypedElement(_modelInfoProvider)).ToList();

                        _searchParameterDefinitionManager.AddNewSearchParameters(searchParams);
                        await _searchParameterStatusManager.AddSearchParameterStatusAsync(searchParams.Select(s => s.GetStringScalar("url")).ToList());
                    }
                }
                while (continuationToken != null);
            }
        }
    }
}
