// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Specification.Summary;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Storage;
using Microsoft.Health.Fhir.Core.Messages.CapabilityStatement;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    /// <summary>
    /// Provides profiles by fetching them from the server.
    /// </summary>
    public sealed class ServerProvideProfileValidation : IProvideProfilesForValidation, IDisposable
    {
        private static HashSet<string> _supportedTypes = new HashSet<string>() { "ValueSet", "StructureDefinition", "CodeSystem" };

        private readonly SemaphoreSlim _cacheSemaphore = new SemaphoreSlim(1, 1);
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly ValidateOperationConfiguration _validateOperationConfig;
        private readonly FhirMemoryCache<Resource> _resourcesByUri;
        private readonly IMediator _mediator;
        private List<ArtifactSummary> _summaries = new List<ArtifactSummary>();
        private DateTime _expirationTime;
        private ILogger<ServerProvideProfileValidation> _logger;

        public ServerProvideProfileValidation(
            Func<IScoped<ISearchService>> searchServiceFactory,
            IOptions<ValidateOperationConfiguration> options,
            IMediator mediator,
            ILogger<ServerProvideProfileValidation> logger)
        {
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(options?.Value, nameof(options));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchServiceFactory = searchServiceFactory;
            _expirationTime = DateTime.UtcNow;
            _validateOperationConfig = options.Value;
            _mediator = mediator;
            _logger = logger;

            _resourcesByUri = new FhirMemoryCache<Resource>(
                nameof(ServerProvideProfileValidation),
                50,
                TimeSpan.FromDays(1000),
                _logger,
                limitType: FhirCacheLimitType.Count);
        }

        public IReadOnlySet<string> GetProfilesTypes() => _supportedTypes;

        public void Refresh()
        {
            _logger.LogInformation("Marking profiles for refresh");
            _expirationTime = DateTime.UtcNow.AddMilliseconds(-1);
        }

        private async Task<IEnumerable<ArtifactSummary>> ListSummariesAsync(CancellationToken cancellationToken, bool resetStatementIfNew = true, bool disablePull = false)
        {
            if (disablePull || _expirationTime >= DateTime.UtcNow)
            {
                return _summaries;
            }

            await _cacheSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_expirationTime >= DateTime.UtcNow)
                {
                    return _summaries;
                }

                _logger.LogDebug("Profile cache expired, updating.");

                _resourcesByUri.Clear();

                var oldHash = resetStatementIfNew ? GetHashForSupportedProfiles(_summaries) : string.Empty;
                var result = await GetSummariesAsync(cancellationToken);
                _summaries = result;
                var newHash = resetStatementIfNew ? GetHashForSupportedProfiles(_summaries) : string.Empty;
                _expirationTime = DateTime.UtcNow.AddSeconds(_validateOperationConfig.CacheDurationInSeconds);

                if (newHash != oldHash)
                {
                    _logger.LogDebug("New Profiles found.");
                    await _mediator.PublishAsync(new RebuildCapabilityStatement(RebuildPart.Profiles));
                }

                _logger.LogDebug("Profiles updated.");
                return _summaries;
            }
            finally
            {
                _cacheSemaphore.Release();
            }
        }

        private async Task<List<ArtifactSummary>> GetSummariesAsync(CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, ArtifactSummary>();
            using (IScoped<ISearchService> searchService = _searchServiceFactory())
            {
                foreach (var type in _supportedTypes)
                {
                    string ct = null;
                    {
                        do
                        {
                            var queryParameters = new List<Tuple<string, string>>();
                            if (ct != null)
                            {
                                ct = ContinuationTokenEncoder.Encode(ct);
                                queryParameters.Add(new Tuple<string, string>(KnownQueryParameterNames.ContinuationToken, ct));
                            }

                            var searchResult = await searchService.Value.SearchAsync(type, queryParameters, cancellationToken);
                            foreach (var searchItem in searchResult.Results)
                            {
                                using (var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(searchItem.Resource.RawResource.Data)))
                                {
                                    using var navStream = new JsonNavigatorStream(memoryStream);
                                    Action<ArtifactSummaryPropertyBag> setOrigin =
                                        (properties) =>
                                        {
                                            properties[ArtifactSummaryProperties.OriginKey] = searchItem.Resource.RawResource.Data;
                                        };

#if Stu3
                                    List<ArtifactSummary> artifacts = ArtifactSummaryGenerator.Default.Generate(navStream, setOrigin);
#else
                                    List<ArtifactSummary> artifacts = new ArtifactSummaryGenerator(ModelInfo.ModelInspector).Generate(navStream, setOrigin);
#endif

                                    foreach (var artifact in artifacts)
                                    {
                                        result[artifact.ResourceUri] = artifact;
                                    }
                                }
                            }

                            ct = searchResult.ContinuationToken;
                        }
                        while (ct != null);
                    }
                }

                return result.Values.ToList();
            }
        }

        private Resource LoadBySummary(ArtifactSummary summary)
        {
            if (summary == null)
            {
                return null;
            }

            if (_resourcesByUri.TryGet(summary.ResourceUri, out Resource resource))
            {
                return resource;
            }

            using (var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(summary.Origin)))
            using (var navStream = new JsonNavigatorStream(memoryStream))
            {
                if (navStream.Seek(summary.Position))
                {
                    if (navStream.Current != null)
                    {
                        resource = navStream.Current.ToPoco<Resource>();
                        _resourcesByUri.TryAdd(summary.ResourceUri, resource);
                        return resource;
                    }
                }
            }

            return null;
        }

        public async Task<Resource> ResolveByCanonicalUriAsync(string uri)
        {
            var summary = (await ListSummariesAsync(CancellationToken.None)).ResolveByCanonicalUri(uri);
            return LoadBySummary(summary);
        }

        public async Task<Resource> ResolveByUriAsync(string uri)
        {
            var summary = (await ListSummariesAsync(CancellationToken.None)).ResolveByUri(uri);
            return LoadBySummary(summary);
        }

        public async Task<IEnumerable<string>> GetSupportedProfilesAsync(string resourceType, CancellationToken cancellationToken, bool disableCacheRefresh = false)
        {
            IEnumerable<ArtifactSummary> summary = await ListSummariesAsync(cancellationToken, false, disableCacheRefresh);
            return summary.Where(x => x.ResourceTypeName == KnownResourceTypes.StructureDefinition)
                .Where(x =>
                    {
                        if (!x.TryGetValue(StructureDefinitionSummaryProperties.TypeKey, out object type))
                        {
                            return false;
                        }

                        return string.Equals((string)type, resourceType, StringComparison.OrdinalIgnoreCase);
                    })
                .Select(x => x.ResourceUri).ToList();
        }

        private static string GetHashForSupportedProfiles(IReadOnlyCollection<ArtifactSummary> summaries)
        {
            if (summaries == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            summaries.Where(x => x.ResourceTypeName == KnownResourceTypes.StructureDefinition)
               .Where(x => x.TryGetValue(StructureDefinitionSummaryProperties.TypeKey, out object type))
               .Select(x => x.ResourceUri).ToList().ForEach(url => sb.Append(url));

            return sb.ToString().ComputeHash();
        }

        public void Dispose()
        {
            _resourcesByUri?.Dispose();
            _cacheSemaphore?.Dispose();
        }
    }
}
