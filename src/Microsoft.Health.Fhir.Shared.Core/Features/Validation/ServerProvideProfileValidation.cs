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
using MediatR;
using Microsoft.Extensions.Hosting;
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
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    /// <summary>
    /// Provides profiles by fetching them from the server.
    /// </summary>
    public sealed class ServerProvideProfileValidation : IProvideProfilesForValidation, IDisposable
    {
        private static HashSet<string> _supportedTypes = new HashSet<string>() { "ValueSet", "StructureDefinition", "CodeSystem" };
        private static string _structureDefinitionVersionKey = "Conformance.version";

        private readonly Task _backgroundTask;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly SemaphoreSlim _cacheSemaphore = new SemaphoreSlim(1, 1);
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly ValidateOperationConfiguration _validateOperationConfig;
        private readonly FhirMemoryCache<Resource> _resourcesByUri;
        private readonly IMediator _mediator;

        private bool _isDisposed = false;
        private string _mostRecentProfileHash = string.Empty;
        private bool _isExternalDependentSyncRequired = false;
        private List<ArtifactSummary> _summaries = new List<ArtifactSummary>();
        private DateTime _expirationTime;
        private ILogger<ServerProvideProfileValidation> _logger;

        public ServerProvideProfileValidation(
            Func<IScoped<ISearchService>> searchServiceFactory,
            IOptions<ValidateOperationConfiguration> options,
            IMediator mediator,
            IHostApplicationLifetime hostApplicationLifetime,
            ILogger<ServerProvideProfileValidation> logger)
        {
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(options?.Value, nameof(options));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(hostApplicationLifetime, nameof(hostApplicationLifetime));

            _searchServiceFactory = searchServiceFactory;
            _expirationTime = DateTime.UtcNow;
            _validateOperationConfig = options.Value;
            _mediator = mediator;
            _logger = logger;

            _resourcesByUri = new FhirMemoryCache<Resource>(
                nameof(ServerProvideProfileValidation),
                sizeLimit: 500,
                entryExpirationTime: TimeSpan.FromDays(1000),
                logger: _logger,
                limitType: FhirCacheLimitType.Count);

            // Setting up background task to monitor profile changes.
            // The background task will only be created if the interval is higher than zero.
            if (_validateOperationConfig.BackgroundProfileStatusCheckIntervalInSeconds > 0)
            {
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(hostApplicationLifetime.ApplicationStopping);
                _backgroundTask = Task.Run(() => BackgroundLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
            }
        }

        public IReadOnlySet<string> GetProfilesTypes() => _supportedTypes;

        public bool IsSyncRequested()
        {
            return _isExternalDependentSyncRequired;
        }

        public void Refresh()
        {
            _logger.LogInformation("Profiles: Marking profiles for refresh");
            _expirationTime = DateTime.UtcNow.AddMilliseconds(-1);
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                if (_cancellationTokenSource?.IsCancellationRequested == false)
                {
                    _cancellationTokenSource.Cancel();
                }

                _resourcesByUri?.Dispose();
                _cacheSemaphore?.Dispose();
                _cancellationTokenSource?.Dispose();

                _isDisposed = true;
            }
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
            // Mark that sync is no longer required as we are fetching the latest profiles.
            _isExternalDependentSyncRequired = false;

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
                .Select(x => GetCanonicalUrl(x)).ToList();
        }

        private static string GetCanonicalUrl(ArtifactSummary artifact)
        {
            var url = artifact.ResourceUri;
            if (artifact.TryGetValue(_structureDefinitionVersionKey, out object version) && version != null && !string.IsNullOrEmpty(version.ToString()))
            {
                return $"{url}|{version}";
            }

            return url;
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
               .Select(x => GetCanonicalUrl(x)).ToList().ForEach(url => sb.Append(url));

            return sb.ToString().ComputeHash();
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

                _logger.LogDebug("Profiles: cache expired, updating.");

                _resourcesByUri.Clear();

                var oldHash = resetStatementIfNew ? GetHashForSupportedProfiles(_summaries) : string.Empty;
                var result = await GetSummariesAsync(cancellationToken);
                _summaries = result;
                var newHash = resetStatementIfNew ? GetHashForSupportedProfiles(_summaries) : string.Empty;
                _expirationTime = DateTime.UtcNow.AddSeconds(_validateOperationConfig.CacheDurationInSeconds);

                if (newHash != oldHash)
                {
                    _logger.LogDebug("Profiles: New Profiles found.");
                    await _mediator.Publish(new RebuildCapabilityStatement(RebuildPart.Profiles));
                }

                _logger.LogInformation("Profiles: Profiles are updated. {CountOfProfiles} Profile(s) are loaded in memory.", _summaries.Count);
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

        private async Task BackgroundLoop(CancellationToken cancellationToken)
        {
            // Waiting for the service to be fully started.
            // At this time profiles should have been loaded at least once.
            await Task.Delay(TimeSpan.FromSeconds(_validateOperationConfig.BackgroundProfileStatusDelayedStartInSeconds), cancellationToken);

            while (true)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                try
                {
                    string profileHash = await GetMostRecentProfileHashAsync(cancellationToken);
                    stopwatch.Stop();

                    if (string.IsNullOrEmpty(_mostRecentProfileHash))
                    {
                        _mostRecentProfileHash = profileHash;
                        _logger.LogInformation("Profiles: Initial profile hash recorded. Hash: {ProfileHash}. Elapsed time: {ElapsedTime}ms", profileHash, stopwatch.ElapsedMilliseconds);
                        _isExternalDependentSyncRequired = true;
                    }
                    else if (_mostRecentProfileHash != profileHash)
                    {
                        _mostRecentProfileHash = profileHash;
                        _logger.LogInformation("Profiles: Changes detected in the server. Letting dependents know refresh is required. Hash: {ProfileHash}. Elapsed time: {ElapsedTime}ms", profileHash, stopwatch.ElapsedMilliseconds);
                        _isExternalDependentSyncRequired = true;
                    }
                }
                catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError(oce, "Profiles: Background profile status task cancelled. Elapsed time: {ElapsedTime}ms", stopwatch.ElapsedMilliseconds);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Profiles: Background profile status task failed. Elapsed time: {ElapsedTime}ms", stopwatch.ElapsedMilliseconds);
                }

                await Task.Delay(TimeSpan.FromSeconds(_validateOperationConfig.BackgroundProfileStatusCheckIntervalInSeconds), cancellationToken);
            }

            _logger.LogInformation("Profiles: Background profile status task is completed.");
        }

        private async Task<string> GetMostRecentProfileHashAsync(CancellationToken cancellationToken)
        {
            StringBuilder hashBuilder = new StringBuilder();

            using (IScoped<ISearchService> searchService = _searchServiceFactory())
            {
                foreach (var type in _supportedTypes)
                {
                    string lastUpdatedStringPerType = "none";
                    long countPerType = 0;

                    var queryParameters = new List<Tuple<string, string>>
                    {
                        new Tuple<string, string>(KnownQueryParameterNames.Sort, "-_lastUpdated"),
                        new Tuple<string, string>(KnownQueryParameterNames.Count, "1"),
                        new Tuple<string, string>(KnownQueryParameterNames.Elements, "id,lastModified"),
                    };
                    var searchResult = await searchService.Value.SearchAsync(type, queryParameters, cancellationToken);
                    if (searchResult?.Results?.Any() == true)
                    {
                        lastUpdatedStringPerType = searchResult.Results.First().Resource.LastModified.ToString("o");
                    }

                    queryParameters = new List<Tuple<string, string>>
                    {
                        new Tuple<string, string>(KnownQueryParameterNames.Summary, "count"),
                    };
                    searchResult = await searchService.Value.SearchAsync(type, queryParameters, cancellationToken);
                    countPerType = searchResult?.TotalCount ?? 0;

                    hashBuilder.Append(string.Concat("{", type, ",", countPerType, ",", lastUpdatedStringPerType, "}"));
                }
            }

            return hashBuilder.ToString();
        }
    }
}
