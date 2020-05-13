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
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.Properties;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public class SearchParameterStatusManager : IRequireInitializationOnFirstRequest
    {
        private readonly ISearchParameterRegistry _searchParameterRegistry;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver;
        private readonly ISearchIndexer _indexer;
        private readonly ISearchParameterRegistry _filebasedRegistry;
        private readonly IMediator _mediator;
        private readonly ILogger<SearchParameterStatusManager> _logger;

        public SearchParameterStatusManager(
            ISearchParameterRegistry searchParameterRegistry,
            FilebasedSearchParameterRegistry.Resolver filebasedRegistryResolver,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ISearchParameterSupportResolver searchParameterSupportResolver,
            ISearchIndexer indexer,
            IMediator mediator,
            ILogger<SearchParameterStatusManager> logger)
        {
            EnsureArg.IsNotNull(searchParameterRegistry, nameof(searchParameterRegistry));
            EnsureArg.IsNotNull(filebasedRegistryResolver, nameof(filebasedRegistryResolver));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(searchParameterSupportResolver, nameof(searchParameterSupportResolver));
            EnsureArg.IsNotNull(indexer, nameof(indexer));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchParameterRegistry = searchParameterRegistry;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _searchParameterSupportResolver = searchParameterSupportResolver;
            _indexer = indexer;
            _filebasedRegistry = filebasedRegistryResolver.Invoke();
            _mediator = mediator;
            _logger = logger;
        }

        public async Task EnsureInitialized()
        {
            using (_logger.BeginTimedScope("EnsureInitialized"))
            {
                var updated = new List<SearchParameterInfo>();
                var updatedParameters = new List<ResourceSearchParameterStatus>();

                DateTimeOffset typeConvertersBuildDate = AssemblyInformation.BuildDate();
                var typeConvertersHash = _indexer.ComputeTypeConvertersHash();

                var parameters = (await _searchParameterRegistry.GetSearchParameterStatuses())
                    .ToDictionary(x => x.Uri);

                var builtinParameters = (await _filebasedRegistry.GetSearchParameterStatuses())
                    .ToDictionary(x => x.Uri);

                // Set states of known parameters
                foreach (var p in _searchParameterDefinitionManager.AllSearchParameters)
                {
                    if (parameters.TryGetValue(p.Url, out ResourceSearchParameterStatus result))
                    {
                        bool isSearchable = false;
                        bool isSupported;

                        if (string.Equals(result.TypeConvertersHash, typeConvertersHash, StringComparison.Ordinal))
                        {
                            // Hash is the same, use value from registry
                            isSupported = result.Status.IsSupported();
                            isSearchable = result.Status.IsSearchable();
                        }
                        else
                        {
                            if (builtinParameters.TryGetValue(p.Url, out var builtinResult))
                            {
                                isSupported = builtinResult.Status.IsSupported();
                            }
                            else
                            {
                                // Registry converter information is different, re-check parameter support.
                                isSupported = _searchParameterSupportResolver.IsSearchParameterSupported(p);
                            }

                            isSearchable = isSupported && result.Status.IsSearchable();

                            // If this is a newer build, update the registry
                            if (typeConvertersBuildDate > result.TypeConvertersBuildDate)
                            {
                                result.TypeConvertersHash = typeConvertersHash;
                                result.TypeConvertersBuildDate = typeConvertersBuildDate;

                                if (isSupported && result.Status == SearchParameterStatus.Disabled)
                                {
                                    result.Status = SearchParameterStatus.Supported;
                                }

                                updatedParameters.Add(result);
                            }
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
                        var isNewDatabase = parameters.Count == 0;

                        // Check if this parameter is now supported.
                        if (builtinParameters.TryGetValue(p.Url, out var builtinResult))
                        {
                            p.IsSupported = builtinResult.Status.IsSupported();
                        }
                        else
                        {
                            // Registry converter information is different, re-check parameter support.
                            p.IsSupported = _searchParameterSupportResolver.IsSearchParameterSupported(p);
                        }

                        p.IsSearchable = p.IsSupported && isNewDatabase;
                        updated.Add(p);

                        updatedParameters.Add(new ResourceSearchParameterStatus
                        {
                            Uri = p.Url,
                            LastUpdated = Clock.UtcNow,
                            TypeConvertersHash = typeConvertersHash,
                            TypeConvertersBuildDate = typeConvertersBuildDate,
                            Status = p.FromSearchParameter(),
                        });
                    }
                }

                if (updatedParameters.Any())
                {
                    await _searchParameterRegistry.UpdateStatuses(updatedParameters);
                }

                await _mediator.Publish(new SearchParametersUpdated(updated));
            }
        }
    }
}
