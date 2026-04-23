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
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    /// <summary>
    /// Provides instantiation capability for US Core profiles.
    /// </summary>
    public class USCoreInstantiateCapability : IInstantiateCapability
    {
        internal const string BaseUrl = "http://hl7.org/fhir/us/core/CapabilityStatement/us-core-server";
        internal const string UrlPrefix = "http://hl7.org/fhir/us/core/StructureDefinition/";
        internal const string UnknownVersion = "unknown";

        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly IResourceDeserializer _resourceDeserializer;
        private readonly ILogger<USCoreInstantiateCapability> _logger;

        public USCoreInstantiateCapability(
            Func<IScoped<ISearchService>> searchServiceFactory,
            IResourceDeserializer resourceDeserializer,
            ILogger<USCoreInstantiateCapability> logger)
        {
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchServiceFactory = searchServiceFactory;
            _resourceDeserializer = resourceDeserializer;
            _logger = logger;
        }

        public async Task<ICollection<string>> GetCanonicalUrlsAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (IScoped<ISearchService> searchService = _searchServiceFactory())
                {
                    _logger.LogInformation("Searching US Core profiles...");

                    var result = default(SearchResult);
                    var continuationToken = default(string);
                    var versions = new HashSet<string>();
                    do
                    {
                        var parameters = new List<Tuple<string, string>>
                        {
                            Tuple.Create("url:below", UrlPrefix),
                        };

                        if (!string.IsNullOrWhiteSpace(continuationToken))
                        {
                            parameters.Add(Tuple.Create(
                                KnownQueryParameterNames.ContinuationToken,
                                ContinuationTokenEncoder.Encode(continuationToken)));
                        }

                        result = await searchService.Value.SearchAsync(
                            KnownResourceTypes.StructureDefinition,
                            parameters,
                            cancellationToken);

                        continuationToken = result?.ContinuationToken;
                        if (result != null)
                        {
                            foreach (var resource in result.Results.Where(x => x.Resource != null).Select(x => x.Resource))
                            {
                                versions.Add(GetVersion(resource));
                            }
                        }
                    }
                    while (!string.IsNullOrEmpty(continuationToken));

                    _logger.LogInformation("{Count} versions of US Core profiles found: {Versions}", versions.Count, string.Join(",", versions));

                    var urls = new List<string>();
                    foreach (var version in versions.OrderBy(x => x, StringComparer.Ordinal))
                    {
                        urls.Add($"{BaseUrl}|{version}");
                    }

                    return urls;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search US Core 6 profiles.");
                throw;
            }
        }

        private string GetVersion(ResourceWrapper wrapper)
        {
            EnsureArg.IsNotNull(wrapper);

            var resource = _resourceDeserializer.Deserialize(wrapper);
            var version = resource?.Scalar<string>("Resource.version");
            return string.IsNullOrEmpty(version) ? UnknownVersion : version;
        }
    }
}
