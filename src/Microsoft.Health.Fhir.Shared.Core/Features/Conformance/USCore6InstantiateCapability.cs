// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class USCore6InstantiateCapability : IInstantiateCapability
    {
        private static readonly string[] Urls =
        {
            "http://hl7.org/fhir/us/core/CapabilityStatement/us-core-server",
        };

        private const string UrlPrefix = "http://hl7.org/fhir/us/core/StructureDefinition/";
        private const string Version = "6.0.0";

        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly ILogger<USCore6InstantiateCapability> _logger;

        public USCore6InstantiateCapability(
            Func<IScoped<ISearchService>> searchServiceFactory,
            ILogger<USCore6InstantiateCapability> logger)
        {
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchServiceFactory = searchServiceFactory;
            _logger = logger;
        }

        public async Task<ICollection<string>> GetCanonicalUrlsAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (IScoped<ISearchService> searchService = _searchServiceFactory())
                {
                    var parameters = new List<Tuple<string, string>>
                    {
                        Tuple.Create("url:below", UrlPrefix),
                        Tuple.Create("version", Version),
                        Tuple.Create(KnownQueryParameterNames.Summary, "count"),
                    };

                    _logger.LogInformation("Searching US Core 6 prifles...");
                    var result = await searchService.Value.SearchAsync(
                        KnownResourceTypes.StructureDefinition,
                        parameters,
                        cancellationToken);

                    _logger.LogInformation("{Count} US Core 6 prifles found.", result.TotalCount);
                    if (result.TotalCount > 0)
                    {
                        return Urls;
                    }

                    return new List<string>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search US Core 6 profiles.");
                throw;
            }
        }
    }
}
