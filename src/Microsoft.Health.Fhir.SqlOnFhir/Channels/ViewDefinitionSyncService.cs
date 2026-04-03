// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Hl7.Fhir.ElementModel;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;

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
    /// May be called before or after <see cref="ExecuteAsync"/> — both orderings are handled.
    /// </summary>
    public Task Handle(SearchParametersInitializedNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Search parameters initialized. Starting ViewDefinition sync service");
        _isInitialized = true;

        // Start the timer if ExecuteAsync has already created it.
        // If ExecuteAsync hasn't run yet (race condition during startup),
        // it will start the timer when it sees _isInitialized == true.
        _refreshTimer?.Change(TimeSpan.Zero, RefreshInterval);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        // Create the timer. If Handle() already fired (notification arrived before this
        // hosted service started), start it immediately. Otherwise leave it dormant
        // until Handle() starts it.
        TimeSpan dueTime = _isInitialized ? TimeSpan.Zero : Timeout.InfiniteTimeSpan;
        TimeSpan period = _isInitialized ? RefreshInterval : Timeout.InfiniteTimeSpan;
        _refreshTimer = new Timer(OnRefreshTimer, null, dueTime, period);

        if (_isInitialized)
        {
            _logger.LogInformation("ViewDefinition sync: notification already received, starting timer immediately");
        }

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
            _logger.LogInformation(
                "ViewDefinition sync timer fired but skipping (cancelled={Cancelled}, initialized={Initialized})",
                _stoppingToken.IsCancellationRequested,
                _isInitialized);
            return;
        }

        if (!await _refreshSemaphore.WaitAsync(0, _stoppingToken))
        {
            _logger.LogInformation("ViewDefinition sync already in progress. Skipping");
            return;
        }

        try
        {
            _logger.LogInformation("ViewDefinition sync cycle starting");
            await SyncViewDefinitionsAsync(_stoppingToken);
            _logger.LogInformation("ViewDefinition sync cycle completed");
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

        _logger.LogInformation(
            "ViewDefinition sync found {Count} Library resource(s) with ViewDefinition profile",
            result.Results.Count());

        // Build set of ViewDefinition names found in persisted Library resources
        var persistedViewDefs = new Dictionary<string, (string Json, string LibraryId, ViewDefinitionStatus Status, IReadOnlyList<string> SubscriptionIds, MaterializationTarget? Target)>(StringComparer.OrdinalIgnoreCase);

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
                    _logger.LogWarning("Failed to extract ViewDefinition JSON from Library '{Id}'", entry.Resource.ResourceId);
                    continue;
                }

                string? name = ExtractViewDefinitionName(viewDefinitionJson);
                if (name != null)
                {
                    ViewDefinitionStatus status = ExtractMaterializationStatus(entry.Resource);
                    IReadOnlyList<string> subscriptionIds = ExtractSubscriptionIds(entry.Resource);
                    MaterializationTarget? target = ExtractMaterializationTarget(entry.Resource);
                    persistedViewDefs[name] = (viewDefinitionJson, entry.Resource.ResourceId, status, subscriptionIds, target);
                }
                else
                {
                    _logger.LogWarning("ViewDefinition JSON from Library '{Id}' has no 'name' property", entry.Resource.ResourceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse Library '{Id}'", entry.Resource.ResourceId);
            }
        }

        _logger.LogInformation(
            "ViewDefinition sync: {PersistedCount} ViewDefinition(s) parsed from Libraries, {RegisteredCount} currently registered in memory",
            persistedViewDefs.Count,
            _subscriptionManager.GetAllRegistrations().Count);

        // Adopt or update registrations for ViewDefinitions found in storage.
        // This node only updates its in-memory cache — the node that received the client
        // request already handled SQL table creation, population, and subscription setup.
        foreach ((string name, (string json, string libraryId, ViewDefinitionStatus status, IReadOnlyList<string> subscriptionIds, MaterializationTarget? target)) in persistedViewDefs)
        {
            ViewDefinitionRegistration? existing = _subscriptionManager.GetRegistration(name);

            if (existing == null)
            {
                // Skip if recently evicted (prevents re-adopting a just-deleted ViewDefinition
                // before the soft-deleted Library disappears from search results)
                if (_recentlyEvicted.TryGetValue(name, out DateTimeOffset evictedAt)
                    && DateTimeOffset.UtcNow - evictedAt < TimeSpan.FromSeconds(30))
                {
                    _logger.LogInformation("ViewDefinition '{ViewDefName}' recently evicted, skipping adoption", name);
                    continue;
                }

                // Another node registered this — adopt into our local cache
                _logger.LogInformation(
                    "Adopting ViewDefinition '{ViewDefName}' from Library '{LibraryId}' with status '{Status}' and target '{Target}'",
                    name,
                    libraryId,
                    status,
                    target?.ToString() ?? "default");

                try
                {
                    await _subscriptionManager.AdoptAsync(json, libraryId, cancellationToken, status, subscriptionIds, target);
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
                    await _subscriptionManager.AdoptAsync(json, libraryId, cancellationToken, status, subscriptionIds, target);
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
    /// Handles both POCO-based element models (where base64Binary Value is byte[])
    /// and JSON-based element models (where Value is a base64 string).
    /// </summary>
    private string? ExtractViewDefinitionJson(ResourceWrapper wrapper)
    {
        ResourceElement element = _resourceDeserializer.Deserialize(wrapper);
        ITypedElement typedElement = element.Instance;

        ITypedElement? contentElement = typedElement.Children("content").FirstOrDefault();
        if (contentElement == null)
        {
            _logger.LogWarning("Library '{ResourceId}' has no content element", wrapper.ResourceId);
            return null;
        }

        string? ct = contentElement.Children("contentType").FirstOrDefault()?.Value?.ToString();
        if (!string.Equals(ct, ViewDefinitionSubscriptionManager.ViewDefinitionContentType, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Library '{ResourceId}' content type '{ContentType}' does not match expected '{Expected}'",
                wrapper.ResourceId,
                ct,
                ViewDefinitionSubscriptionManager.ViewDefinitionContentType);
            return null;
        }

        object? dataValue = contentElement.Children("data").FirstOrDefault()?.Value;
        if (dataValue == null)
        {
            _logger.LogWarning("Library '{ResourceId}' has no data element in content", wrapper.ResourceId);
            return null;
        }

        // POCO-based element model returns byte[] for base64Binary fields;
        // JSON-based element model returns the raw base64 string.
        if (dataValue is byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        string? base64 = dataValue.ToString();
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

    /// <summary>
    /// Extracts the materialization status from the Library resource's extension.
    /// Returns <see cref="ViewDefinitionStatus.Active"/> if the extension is not present
    /// (backward compatibility for Libraries created before status persistence was added).
    /// </summary>
    private static ViewDefinitionStatus ExtractMaterializationStatus(ResourceWrapper wrapper)
    {
        try
        {
            using var doc = JsonDocument.Parse(wrapper.RawResource.Data);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("extension", out JsonElement extensions))
            {
                foreach (JsonElement ext in extensions.EnumerateArray())
                {
                    if (ext.TryGetProperty("url", out JsonElement url)
                        && string.Equals(
                            url.GetString(),
                            ViewDefinitionSubscriptionManager.MaterializationStatusExtensionUrl,
                            StringComparison.OrdinalIgnoreCase)
                        && ext.TryGetProperty("valueCode", out JsonElement valueCode))
                    {
                        string? statusStr = valueCode.GetString();
                        if (Enum.TryParse<ViewDefinitionStatus>(statusStr, ignoreCase: true, out var status))
                        {
                            return status;
                        }
                    }
                }
            }
        }
        catch
        {
            // Fall through to default
        }

        return ViewDefinitionStatus.Active;
    }

    /// <summary>
    /// Extracts the materialization target from a Library resource's extension.
    /// Returns <c>null</c> if not specified, which lets the caller fall back to the
    /// server-wide <see cref="SqlOnFhirMaterializationConfiguration.DefaultTarget"/>.
    /// </summary>
    private static MaterializationTarget? ExtractMaterializationTarget(ResourceWrapper wrapper)
    {
        try
        {
            using var doc = JsonDocument.Parse(wrapper.RawResource.Data);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("extension", out JsonElement extensions))
            {
                foreach (JsonElement ext in extensions.EnumerateArray())
                {
                    if (ext.TryGetProperty("url", out JsonElement url)
                        && string.Equals(
                            url.GetString(),
                            ViewDefinitionSubscriptionManager.MaterializationTargetExtensionUrl,
                            StringComparison.OrdinalIgnoreCase)
                        && ext.TryGetProperty("valueCode", out JsonElement valueCode))
                    {
                        string? targetStr = valueCode.GetString();
                        if (Enum.TryParse<MaterializationTarget>(targetStr, ignoreCase: true, out var target))
                        {
                            return target;
                        }
                    }
                }
            }
        }
        catch
        {
            // Fall through to default
        }

        return null;
    }

    /// <summary>
    /// Extracts auto-created Subscription resource IDs from the Library resource's
    /// <c>relatedArtifact</c> entries where type is <c>depends-on</c> and the resource
    /// reference starts with <c>Subscription/</c>.
    /// </summary>
    private static List<string> ExtractSubscriptionIds(ResourceWrapper wrapper)
    {
        var ids = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(wrapper.RawResource.Data);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("relatedArtifact", out JsonElement artifacts))
            {
                foreach (JsonElement artifact in artifacts.EnumerateArray())
                {
                    if (artifact.TryGetProperty("type", out JsonElement type)
                        && string.Equals(type.GetString(), "depends-on", StringComparison.OrdinalIgnoreCase)
                        && artifact.TryGetProperty("resource", out JsonElement resource))
                    {
                        string? resourceRef = resource.GetString();
                        if (resourceRef != null
                            && resourceRef.StartsWith("Subscription/", StringComparison.OrdinalIgnoreCase))
                        {
                            ids.Add(resourceRef.Substring("Subscription/".Length));
                        }
                    }
                }
            }
        }
        catch
        {
            // Fall through to empty list
        }

        return ids;
    }

    private static string ComputeHash(string content)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash);
    }
}
