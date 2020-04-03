// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public class FilebasedSearchParameterRegistry : ISearchParameterRegistry
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly Assembly _resourceAssembly;
        private readonly string _unsupportedParamsEmbeddedResourceName;
        private WeakReference<ResourceSearchParameterStatus[]> _statusResults;
        private object sync = new object();

        public FilebasedSearchParameterRegistry(
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            Assembly resourceAssembly,
            string unsupportedParamsEmbeddedResourceName)
        {
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(resourceAssembly, nameof(resourceAssembly));
            EnsureArg.IsNotNullOrWhiteSpace(unsupportedParamsEmbeddedResourceName, nameof(unsupportedParamsEmbeddedResourceName));

            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _resourceAssembly = resourceAssembly;
            _unsupportedParamsEmbeddedResourceName = unsupportedParamsEmbeddedResourceName;
        }

        public Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses()
        {
            ResourceSearchParameterStatus[] statusResults = null;

            if (_statusResults?.TryGetTarget(out statusResults) != true)
            {
                lock (sync)
                {
                    if (_statusResults?.TryGetTarget(out statusResults) != true)
                    {
                        using Stream stream = _resourceAssembly.GetManifestResourceStream(_unsupportedParamsEmbeddedResourceName);
                        using TextReader reader = new StreamReader(stream);
                        var unsupportedParams = JsonConvert.DeserializeObject<UnsupportedSearchParameters>(reader.ReadToEnd());

                        // Loads unsupported parameters
                        var support = unsupportedParams.Unsupported
                            .Select(x => new ResourceSearchParameterStatus
                            {
                                Uri = x,
                                Status = SearchParameterStatus.Disabled,
                                LastUpdated = Clock.UtcNow,
                            })
                            .Concat(unsupportedParams.PartialSupport
                                .Select(x => new ResourceSearchParameterStatus
                                {
                                    Uri = x,
                                    Status = SearchParameterStatus.Enabled,
                                    IsPartiallySupported = true,
                                    LastUpdated = Clock.UtcNow,
                                }))
                            .ToDictionary(x => x.Uri);

                        // Merge with supported list
                        statusResults = _searchParameterDefinitionManager.AllSearchParameters
                            .Where(x => !support.ContainsKey(x.Url))
                            .Select(x => new ResourceSearchParameterStatus
                            {
                                Uri = x.Url,
                                Status = SearchParameterStatus.Enabled,
                                LastUpdated = Clock.UtcNow,
                            })
                            .Concat(support.Values)
                            .ToArray();

                        _statusResults = new WeakReference<ResourceSearchParameterStatus[]>(statusResults);
                    }
                }
            }

            return Task.FromResult<IReadOnlyCollection<ResourceSearchParameterStatus>>(statusResults);
        }

        public Task UpdateStatuses(IEnumerable<ResourceSearchParameterStatus> statuses)
        {
            return Task.CompletedTask;
        }
    }
}
