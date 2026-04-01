// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Text;
using Hl7.Fhir.ElementModel;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.SqlOnFhir.Channels;

/// <summary>
/// Background service that manages the ViewDefinition registration cache across server restarts
/// and multiple compute nodes. Follows the same pattern as <c>SearchParameterCacheRefreshBackgroundService</c>:
/// <list type="number">
///   <item>Waits for <see cref="SearchParametersInitializedNotification"/> (indicating the FHIR server is ready)</item>
///   <item>Performs initial recovery of persisted ViewDefinition Library resources</item>
///   <item>Polls every 10 seconds for changes (new registrations or deletions from other nodes)</item>
/// </list>
/// </summary>
public sealed class ViewDefinitionSyncService : BackgroundService,
    INotificationHandler<SearchParametersInitializedNotification>
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
    private readonly IResourceDeserializer _resourceDeserializer;
    private readonly IViewDefinitionSubscriptionManager _subscriptionManager;
    private readonly ILogger<ViewDefinitionSyncService> _logger;
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentlyEvicted = new(StringComparer.OrdinalIgnoreCase);

    private Timer? _refreshTimer;
    private CancellationToken _stoppingToken;
    private bool _isInitialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionSyncService"/> class.
    /// </summary>
    public ViewDefinitionSyncService(
        Func<IScoped<ISearchService>> searchServiceFactory,
        IResourceDeserializer resourceDeserializer,
        IViewDefinitionSubscriptionManager subscriptionManager,
        ILogger<ViewDefinitionSyncService> logger)
    {
        _searchServiceFactory = searchServiceFactory;
        _resourceDeserializer = resourceDeserializer;
        _subscriptionManager = subscriptionManager;
        _logger = logger;
    }

    /// <summary>
    /// Called when search parameters are fully initialized, signaling the FHIR server is ready.
    /// This triggers the first ViewDefinition sync and starts the polling timer.
    /// </summary>
    public Task Handle(SearchParametersInitializedNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Search parameters initialized. Starting ViewDefinition sync service");
        _isInitialized = true;

        // Start the timer: first execution immediately (0 delay), then every RefreshInterval
        _refreshTimer?.Change(TimeSpan.Zero, RefreshInterval);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        // Create the timer but don't start it — Handle() starts it after search params are ready
        _refreshTimer = new Timer(OnRefreshTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _refreshTimer?.Dispose();
        _refreshSemaphore.Dispose();
        base.Dispose();
    }

    private async void OnRefreshTimer(object? state)
    {
        if (_stoppingToken.IsCancellationRequested || !_isInitialized)
        {
            return;
        }

        if (!await _refreshSemaphore.WaitAsync(0, _stoppingToken))
        {
            _logger.LogDebug("ViewDefinition sync already in progress. Skipping");
            return;
        }

        try
        {
            await SyncViewDefinitionsAsync(_stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "ViewDefinition sync cycle failed");
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    /// <summary>
    /// Synchronizes the in-memory ViewDefinition registrations with persisted Library resources.
    /// Adds new registrations and removes stale ones.
    /// </summary>
    private async Task SyncViewDefinitionsAsync(CancellationToken cancellationToken)
    {
        using IScoped<ISearchService> scope = _searchServiceFactory();
        ISearchService searchService = scope.Value;

        var queryParameters = new List<Tuple<string, string>>
        {
            Tuple.Create("_profile", ViewDefinitionSubscriptionManager.ViewDefinitionLibraryProfile),
            Tuple.Create("_count", "100"),
        };

        SearchResult result = await searchService.SearchAsync(
            "Library",
            queryParameters,
            cancellationToken);

        // Build set of ViewDefinition names found in persisted Library resources
        var persistedViewDefs = new Dictionary<string, (string Json, string LibraryId)>(StringComparer.OrdinalIgnoreCase);

        foreach (SearchResultEntry entry in result.Results)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                string? viewDefinitionJson = ExtractViewDefinitionJson(entry.Resource);
                if (viewDefinitionJson == null)
                {
                    continue;
                }

                string? name = ExtractViewDefinitionName(viewDefinitionJson);
                if (name != null)
                {
                    persistedViewDefs[name] = (viewDefinitionJson, entry.Resource.ResourceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse Library '{Id}'", entry.Resource.ResourceId);
            }
        }

        // Adopt or update registrations for ViewDefinitions found in storage.
        // This node only updates its in-memory cache — the node that received the client
        // request already handled SQL table creation, population, and subscription setup.
        foreach ((string name, (string json, string libraryId)) in persistedViewDefs)
        {
            ViewDefinitionRegistration? existing = _subscriptionManager.GetRegistration(name);

            if (existing == null)
            {
                // Skip if recently evicted (prevents re-adopting a just-deleted ViewDefinition
                // before the soft-deleted Library disappears from search results)
                if (_recentlyEvicted.TryGetValue(name, out DateTimeOffset evictedAt)
                    && DateTimeOffset.UtcNow - evictedAt < TimeSpan.FromSeconds(30))
                {
                    continue;
                }

                // Another node registered this — adopt into our local cache
                try
                {
                    await _subscriptionManager.AdoptAsync(json, libraryId, cancellationToken);
                    _recentlyEvicted.TryRemove(name, out _);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to adopt ViewDefinition '{ViewDefName}'", name);
                }
            }
            else if (ComputeHash(json) != ComputeHash(existing.ViewDefinitionJson))
            {
                // Another node updated this — refresh our local cache with the new definition
                _logger.LogInformation("ViewDefinition '{ViewDefName}' updated by another node. Refreshing cache", name);

                try
                {
                    _subscriptionManager.Evict(name);
                    await _subscriptionManager.AdoptAsync(json, libraryId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh ViewDefinition '{ViewDefName}'", name);
                }
            }
        }

        // Evict in-memory registrations whose Library resource was deleted by another node
        foreach (ViewDefinitionRegistration registration in _subscriptionManager.GetAllRegistrations())
        {
            if (!persistedViewDefs.ContainsKey(registration.ViewDefinitionName))
            {
                _logger.LogInformation(
                    "ViewDefinition '{ViewDefName}' deleted by another node. Evicting from cache",
                    registration.ViewDefinitionName);

                _subscriptionManager.Evict(registration.ViewDefinitionName);
                _recentlyEvicted[registration.ViewDefinitionName] = DateTimeOffset.UtcNow;
            }
        }
    }

    /// <summary>
    /// Extracts the ViewDefinition JSON from a Library resource's content attachment.
    /// </summary>
    private string? ExtractViewDefinitionJson(ResourceWrapper wrapper)
    {
        ResourceElement element = _resourceDeserializer.Deserialize(wrapper);
        ITypedElement typedElement = element.Instance;

        ITypedElement? contentElement = typedElement.Children("content").FirstOrDefault();
        if (contentElement == null)
        {
            return null;
        }

        string? ct = contentElement.Children("contentType").FirstOrDefault()?.Value?.ToString();
        if (!string.Equals(ct, ViewDefinitionSubscriptionManager.ViewDefinitionContentType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? base64 = contentElement.Children("data").FirstOrDefault()?.Value?.ToString();
        if (string.IsNullOrEmpty(base64))
        {
            return null;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private static string? ExtractViewDefinitionName(string viewDefinitionJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(viewDefinitionJson);
            return doc.RootElement.TryGetProperty("name", out var name) ? name.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeHash(string content)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash);
    }
}
