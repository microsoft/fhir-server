// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public class SearchParameterResourceDataStore : IHostedService
    {
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ILogger _logger;

        public SearchParameterResourceDataStore(
            ISearchParameterOperations searchParameterOperations,
            Func<IScoped<ISearchService>> searchServiceFactory,
            IModelInfoProvider modelInfoProvider,
            ILogger<SearchParameterResourceDataStore> logger)
        {
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchParameterOperations = searchParameterOperations;
            _searchServiceFactory = searchServiceFactory;
            _modelInfoProvider = modelInfoProvider;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
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

                    var result = await search.Value.SearchAsync("SearchParameter", queryParams, cancellationToken);
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

                        foreach (var searchParam in searchParams)
                        {
                            try
                            {
                                await _searchParameterOperations.AddSearchParameterAsync(searchParam);
                            }
                            catch (SearchParameterNotSupportedException ex)
                            {
                                _logger.LogWarning(ex, $"Error loading search parameter {searchParam.GetStringScalar("url")} from data store.");
                            }
                            catch (InvalidDefinitionException ex)
                            {
                                _logger.LogWarning(ex, $"Error loading search parameter {searchParam.GetStringScalar("url")} from data store.");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error loading search parameter {searchParam.GetStringScalar("url")} from data store.");
                            }
                        }
                    }
                }
                while (continuationToken != null);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
