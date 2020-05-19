// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public class FilebasedSearchParameterRegistryDataStore : ISearchParameterRegistryDataStore
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly Assembly _resourceAssembly;
        private readonly string _unsupportedParamsEmbeddedResourceName;
        private ResourceSearchParameterStatus[] _statusResults;

        public FilebasedSearchParameterRegistryDataStore(
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

        public delegate ISearchParameterRegistryDataStore Resolver();

        public Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses(CancellationToken cancellationToken)
        {
            if (_statusResults == null)
            {
                using Stream stream = _resourceAssembly.GetManifestResourceStream(_unsupportedParamsEmbeddedResourceName);
                using TextReader reader = new StreamReader(stream);
                UnsupportedSearchParameters unsupportedParams = JsonConvert.DeserializeObject<UnsupportedSearchParameters>(reader.ReadToEnd());

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
                _statusResults = _searchParameterDefinitionManager.AllSearchParameters
                    .Where(x => !support.ContainsKey(x.Url))
                    .Select(x => new ResourceSearchParameterStatus
                    {
                        Uri = x.Url,
                        Status = SearchParameterStatus.Enabled,
                        LastUpdated = Clock.UtcNow,
                    })
                    .Concat(support.Values)
                    .ToArray();
            }

            return Task.FromResult<IReadOnlyCollection<ResourceSearchParameterStatus>>(_statusResults);
        }

        public Task UpsertStatuses(IEnumerable<ResourceSearchParameterStatus> statuses, CancellationToken cancellationToken)
        {
            // File based registry does not persist runtime updates
            return Task.CompletedTask;
        }
    }
}
