// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Text.Json;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization.Jobs;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlOnFhir.Channels;

/// <summary>
/// Manages the lifecycle of auto-created Subscription resources for materialized ViewDefinitions.
/// When a ViewDefinition is registered, this manager:
/// 1. Creates the materialized SQL table
/// 2. Enqueues a full population background job
/// 3. Creates a FHIR Subscription resource via the MediatR pipeline (getting full validation)
/// 4. Tracks the 1:N mapping (ViewDefinition → Subscriptions)
/// </summary>
public sealed class ViewDefinitionSubscriptionManager : IViewDefinitionSubscriptionManager,
    INotificationHandler<ViewDefinitionPopulationCompleteNotification>
{
    private const string BackportProfileUrl = "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-subscription";
    private const string TransactionTopicUrl = "http://azurehealthcareapis.com/data-extentions/SubscriptionTopics/transactions";
    private const string BackportFilterCriteriaUrl = "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-filter-criteria";
    private const string BackportChannelTypeUrl = "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-channel-type";
    private const string ChannelTypeCodingSystem = "http://azurehealthcareapis.com/data-extentions/subscription-channel-type";
    private const string BackportPayloadContentUrl = "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-payload-content";
    private const string BackportMaxCountUrl = "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-max-count";

    /// <summary>
    /// Profile URL used to tag Library resources that contain ViewDefinitions.
    /// </summary>
    public const string ViewDefinitionLibraryProfile = "https://sql-on-fhir.org/ig/StructureDefinition/ViewDefinition";

    /// <summary>
    /// Content type for ViewDefinition JSON stored in Library.content.
    /// </summary>
    public const string ViewDefinitionContentType = "application/json+viewdefinition";

    /// <summary>
    /// Extension URL used to persist the materialization lifecycle status on a Library resource.
    /// This allows the status (e.g., Populating, Active) to survive server restarts.
    /// </summary>
    public const string MaterializationStatusExtensionUrl = "https://sql-on-fhir.org/ig/StructureDefinition/materialization-status";

    private readonly ConcurrentDictionary<string, ViewDefinitionRegistration> _registrations = new(StringComparer.OrdinalIgnoreCase);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IViewDefinitionSchemaManager _schemaManager;
    private readonly IQueueClient _queueClient;
    private readonly ILogger<ViewDefinitionSubscriptionManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionSubscriptionManager"/> class.
    /// </summary>
    public ViewDefinitionSubscriptionManager(
        IServiceScopeFactory scopeFactory,
        IViewDefinitionSchemaManager schemaManager,
        IQueueClient queueClient,
        ILogger<ViewDefinitionSubscriptionManager> logger)
    {
        _scopeFactory = scopeFactory;
        _schemaManager = schemaManager;
        _queueClient = queueClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ViewDefinitionRegistration> RegisterAsync(
        string viewDefinitionJson,
        string libraryResourceId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryResourceId);

        (string name, string resourceType) = ExtractViewDefinitionMetadata(viewDefinitionJson);

        // Skip re-registration if the ViewDefinition is already registered with identical content
        if (_registrations.TryGetValue(name, out ViewDefinitionRegistration? existing)
            && existing.ViewDefinitionJson == viewDefinitionJson
            && existing.Status is ViewDefinitionStatus.Active or ViewDefinitionStatus.Populating)
        {
            _logger.LogInformation(
                "ViewDefinition '{ViewDefName}' already registered with same content (status: {Status}). Skipping",
                name,
                existing.Status);

            // Update the Library ID if it changed (e.g., PUT created a new version)
            if (!string.IsNullOrEmpty(libraryResourceId))
            {
                existing.LibraryResourceId = libraryResourceId;
            }

            return existing;
        }

        _logger.LogInformation(
            "Registering ViewDefinition '{ViewDefName}' for materialization (resource type: {ResourceType})",
            name,
            resourceType);

        // Step 1: Create the materialized SQL table
        var registration = new ViewDefinitionRegistration
        {
            ViewDefinitionJson = viewDefinitionJson,
            ViewDefinitionName = name,
            ResourceType = resourceType,
            Status = ViewDefinitionStatus.Creating,
        };

        _registrations[name] = registration;

        try
        {
            if (!await _schemaManager.TableExistsAsync(name, cancellationToken))
            {
                await _schemaManager.CreateTableAsync(viewDefinitionJson, cancellationToken);
            }

            // Step 2: Enqueue full population background job.
            // This is best-effort — the subscription will handle incremental updates even if
            // the initial population job fails to enqueue.
            registration.Status = ViewDefinitionStatus.Populating;

            try
            {
                var populationDef = new ViewDefinitionPopulationOrchestratorJobDefinition
                {
                    ViewDefinitionJson = viewDefinitionJson,
                    ViewDefinitionName = name,
                    ResourceType = resourceType,
                    BatchSize = 100,
                    LibraryResourceId = libraryResourceId,
                };

                await _queueClient.EnqueueAsync(
                    (byte)QueueType.ViewDefinitionPopulation,
                    new[] { JsonConvert.SerializeObject(populationDef) },
                    groupId: null,
                    forceOneActiveJobGroup: false,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to enqueue population job for '{ViewDefName}'. Incremental updates via subscription will still work", name);
            }

            // Step 3: Create Subscription resource via MediatR pipeline
            string subscriptionId = await CreateSubscriptionAsync(viewDefinitionJson, name, resourceType, cancellationToken);
            registration.SubscriptionIds.Add(subscriptionId);

            registration.LibraryResourceId = libraryResourceId;

            // Persist the populating status and subscription reference to the Library resource.
            await UpdateLibraryMaterializationStatusAsync(
                libraryResourceId, ViewDefinitionStatus.Populating, cancellationToken, registration.SubscriptionIds);

            // Status stays as Populating — the ViewDefinitionPopulationProcessingJob will
            // publish ViewDefinitionPopulationCompleteNotification when done, which triggers
            // the Handle method above to set status to Active (or Error).

            _logger.LogInformation(
                "ViewDefinition '{ViewDefName}' registered with Subscription '{SubscriptionId}'. Status: Populating",
                name,
                subscriptionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            registration.Status = ViewDefinitionStatus.Error;
            registration.ErrorMessage = ex.Message;

            _logger.LogError(ex, "Failed to register ViewDefinition '{ViewDefName}'", name);
            throw;
        }

        return registration;
    }

    /// <inheritdoc />
    public async Task UnregisterAsync(string viewDefinitionName, bool dropTable, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionName);

        if (!_registrations.TryRemove(viewDefinitionName, out ViewDefinitionRegistration? registration))
        {
            _logger.LogWarning("ViewDefinition '{ViewDefName}' is not registered", viewDefinitionName);
            return;
        }

        // Delete auto-created Subscription resources.
        // First try the in-memory IDs; if empty (e.g., after restart/adoption), search by endpoint.
        IEnumerable<string> subscriptionIds = registration.SubscriptionIds;
        if (!subscriptionIds.Any())
        {
            subscriptionIds = await FindSubscriptionIdsByEndpointAsync(viewDefinitionName, cancellationToken);
        }

        foreach (string subscriptionId in subscriptionIds)
        {
            try
            {
                await SendScopedAsync(
                    new DeleteResourceRequest(KnownResourceTypes.Subscription, subscriptionId, DeleteOperation.SoftDelete),
                    cancellationToken);

                _logger.LogInformation("Deleted auto-created Subscription '{SubscriptionId}'", subscriptionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete Subscription '{SubscriptionId}'", subscriptionId);
            }
        }

        // Delete the persisted Library resource (if not already being deleted by the caller)
        if (!string.IsNullOrEmpty(registration.LibraryResourceId))
        {
            try
            {
                await SendScopedAsync(
                    new DeleteResourceRequest("Library", registration.LibraryResourceId, DeleteOperation.SoftDelete),
                    cancellationToken);

                _logger.LogInformation("Deleted Library resource '{LibraryId}' for ViewDefinition '{ViewDefName}'", registration.LibraryResourceId, viewDefinitionName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete Library resource '{LibraryId}'", registration.LibraryResourceId);
            }
        }

        // Optionally drop the materialized table
        if (dropTable)
        {
            await _schemaManager.DropTableAsync(viewDefinitionName, cancellationToken);
        }

        _logger.LogInformation("Unregistered ViewDefinition '{ViewDefName}'", viewDefinitionName);
    }

    /// <inheritdoc />
    public ViewDefinitionRegistration? GetRegistration(string viewDefinitionName)
    {
        _registrations.TryGetValue(viewDefinitionName, out ViewDefinitionRegistration? registration);
        return registration;
    }

    /// <inheritdoc />
    public IReadOnlyList<ViewDefinitionRegistration> GetAllRegistrations()
    {
        return _registrations.Values.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<ViewDefinitionRegistration> AdoptAsync(
        string viewDefinitionJson,
        string? libraryResourceId,
        CancellationToken cancellationToken,
        ViewDefinitionStatus initialStatus = ViewDefinitionStatus.Active,
        IReadOnlyList<string>? subscriptionIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionJson);

        (string name, string resourceType) = ExtractViewDefinitionMetadata(viewDefinitionJson);

        var registration = new ViewDefinitionRegistration
        {
            ViewDefinitionJson = viewDefinitionJson,
            ViewDefinitionName = name,
            ResourceType = resourceType,
            LibraryResourceId = libraryResourceId,
            Status = initialStatus,
        };

        if (subscriptionIds != null)
        {
            foreach (string subId in subscriptionIds)
            {
                registration.SubscriptionIds.Add(subId);
            }
        }

        _registrations[name] = registration;

        // Sanity check: verify the materialized table exists (another node should have created it)
        bool tableExists = await _schemaManager.TableExistsAsync(name, cancellationToken);
        if (!tableExists)
        {
            _logger.LogWarning(
                "Adopted ViewDefinition '{ViewDefName}' but materialized table does not exist. " +
                "It may still be creating on another node",
                name);
        }

        _logger.LogInformation(
            "Adopted ViewDefinition '{ViewDefName}' into local cache with status '{Status}'",
            name,
            initialStatus);
        return registration;
    }

    /// <inheritdoc />
    public void Evict(string viewDefinitionName)
    {
        if (_registrations.TryRemove(viewDefinitionName, out _))
        {
            _logger.LogInformation("Evicted ViewDefinition '{ViewDefName}' from local cache", viewDefinitionName);
        }
    }

    /// <summary>
    /// Handles the population complete notification by updating the in-memory registration status
    /// and persisting it to the Library resource.
    /// </summary>
    public async Task Handle(ViewDefinitionPopulationCompleteNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Handle ViewDefinitionPopulationCompleteNotification received for '{ViewDefName}' (success={Success}, rows={Rows})",
            notification.ViewDefinitionName,
            notification.Success,
            notification.RowsInserted);

        if (!_registrations.TryGetValue(notification.ViewDefinitionName, out ViewDefinitionRegistration? registration))
        {
            // This node didn't originate the registration (multi-node scenario).
            // Adopt the ViewDefinition into our local cache before updating status.
            _logger.LogInformation(
                "ViewDefinition '{ViewDefName}' not in local cache. Adopting from Library '{LibraryId}' (multi-node scenario)",
                notification.ViewDefinitionName,
                notification.LibraryResourceId);

            if (!string.IsNullOrEmpty(notification.LibraryResourceId))
            {
                try
                {
                    var getResponse = await SendScopedAsync<GetResourceResponse>(
                        new GetResourceRequest("Library", notification.LibraryResourceId),
                        cancellationToken);

                    string? viewDefJson = ExtractViewDefinitionJsonFromRawResource(getResponse.Resource);
                    if (viewDefJson != null)
                    {
                        registration = await AdoptAsync(
                            viewDefJson,
                            notification.LibraryResourceId,
                            cancellationToken,
                            initialStatus: ViewDefinitionStatus.Populating);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to adopt ViewDefinition '{ViewDefName}' from Library '{LibraryId}' during population complete handling",
                        notification.ViewDefinitionName,
                        notification.LibraryResourceId);
                }
            }
        }

        if (registration != null)
        {
            registration.Status = notification.Success ? ViewDefinitionStatus.Active : ViewDefinitionStatus.Error;
            registration.ErrorMessage = notification.ErrorMessage;

            _logger.LogInformation(
                "ViewDefinition '{ViewDefName}' population complete: {Status} ({Rows} rows)",
                notification.ViewDefinitionName,
                registration.Status,
                notification.RowsInserted);

            // Persist the final status to the Library resource so it survives restarts
            // and is visible to other nodes via the SyncService.
            string libraryId = registration.LibraryResourceId ?? notification.LibraryResourceId;
            if (!string.IsNullOrEmpty(libraryId))
            {
                await UpdateLibraryMaterializationStatusAsync(
                    libraryId,
                    registration.Status,
                    cancellationToken);
            }
        }
        else
        {
            _logger.LogWarning(
                "ViewDefinition '{ViewDefName}' could not be resolved from local cache or database. Status will not be updated",
                notification.ViewDefinitionName);
        }
    }
    }

    /// <summary>
    /// Persists the materialization metadata (status and subscription references) on the Library
    /// resource so it survives server restarts and is visible to other nodes. Subscription IDs
    /// are stored as <c>relatedArtifact</c> entries with type <c>depends-on</c>.
    /// This is best-effort — failures are logged but do not affect in-memory tracking.
    /// </summary>
    private async Task UpdateLibraryMaterializationStatusAsync(
        string libraryResourceId,
        ViewDefinitionStatus status,
        CancellationToken cancellationToken,
        IEnumerable<string>? subscriptionIds = null)
    {
        try
        {
            // Read the current Library resource
            var getResponse = await SendScopedAsync<GetResourceResponse>(
                new GetResourceRequest("Library", libraryResourceId),
                cancellationToken);

            string rawJson = getResponse.Resource.RawResource.Data;
            var parser = new FhirJsonParser();
            Library library = await parser.ParseAsync<Library>(rawJson);

            // Add or update the materialization-status extension
            string statusValue = status.ToString().ToLowerInvariant();
            Extension? existingExt = library.Extension.FirstOrDefault(
                e => e.Url == MaterializationStatusExtensionUrl);

            if (existingExt != null)
            {
                existingExt.Value = new Code(statusValue);
            }
            else
            {
                library.Extension.Add(new Extension(MaterializationStatusExtensionUrl, new Code(statusValue)));
            }

            // Persist subscription IDs as relatedArtifact entries (type=depends-on)
            if (subscriptionIds != null)
            {
                // Remove existing auto-created subscription references
                library.RelatedArtifact.RemoveAll(
                    ra => ra.Type == RelatedArtifact.RelatedArtifactType.DependsOn
                        && ra.Resource != null
                        && ra.Resource.StartsWith("Subscription/", StringComparison.OrdinalIgnoreCase));

                foreach (string subId in subscriptionIds)
                {
                    library.RelatedArtifact.Add(new RelatedArtifact
                    {
                        Type = RelatedArtifact.RelatedArtifactType.DependsOn,
                        Resource = $"Subscription/{subId}",
                        Display = "Auto-created materialization subscription",
                    });
                }
            }

            // Upsert the modified Library back through the pipeline
            var resourceElement = new ResourceElement(library.ToTypedElement());
            await SendScopedAsync<UpsertResourceResponse>(
                new UpsertResourceRequest(resourceElement),
                cancellationToken);

            _logger.LogInformation(
                "Persisted materialization metadata on Library '{LibraryId}' (status={Status}, subscriptions={SubCount})",
                libraryResourceId,
                statusValue,
                subscriptionIds?.Count() ?? 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            const string message = "Failed to persist materialization metadata on Library '{LibraryId}'. "
                + "State is tracked in memory but may be lost on restart";
            _logger.LogWarning(ex, message, libraryResourceId);
        }
    }

    /// <summary>
    /// Searches for auto-created Subscription resources by matching the criteria topic URL
    /// and channel endpoint pattern. Used during cleanup when in-memory subscription IDs are
    /// not available (e.g., after restart or adoption from another node).
    /// </summary>
    private async Task<IReadOnlyList<string>> FindSubscriptionIdsByEndpointAsync(
        string viewDefinitionName,
        CancellationToken cancellationToken)
    {
        try
        {
            string expectedEndpoint = $"internal://sqlfhir/{viewDefinitionName}";

            using IServiceScope scope = _scopeFactory.CreateScope();
            var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();

            // Search for subscriptions with our transaction topic criteria.
            // Then filter client-side by channel endpoint, since endpoint is not a search parameter in R4.
            var queryParameters = new List<Tuple<string, string>>
            {
                Tuple.Create("criteria", TransactionTopicUrl),
                Tuple.Create("_count", "100"),
            };

            SearchResult result = await searchService.SearchAsync(
                "Subscription",
                queryParameters,
                cancellationToken);

            var ids = new List<string>();
            var resourceDeserializer = scope.ServiceProvider.GetRequiredService<IResourceDeserializer>();

            foreach (SearchResultEntry entry in result.Results)
            {
                ResourceElement element = resourceDeserializer.Deserialize(entry.Resource);
                string? endpoint = element.Instance
                    .Children("channel").FirstOrDefault()
                    ?.Children("endpoint").FirstOrDefault()
                    ?.Value?.ToString();

                if (string.Equals(endpoint, expectedEndpoint, StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(entry.Resource.ResourceId);
                }
            }

            if (ids.Count > 0)
            {
                _logger.LogInformation(
                    "Found {Count} auto-created Subscription(s) for ViewDefinition '{ViewDefName}' by endpoint search",
                    ids.Count,
                    viewDefinitionName);
            }

            return ids;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to search for auto-created Subscriptions for ViewDefinition '{ViewDefName}'",
                viewDefinitionName);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Builds a FHIR R4 Subscription resource conforming to the backport profile and
    /// creates it via the MediatR pipeline, which runs subscription validation, handshake,
    /// status management, search indexing, and persistence.
    /// </summary>
    private async Task<string> CreateSubscriptionAsync(
        string viewDefinitionJson,
        string viewDefinitionName,
        string resourceType,
        CancellationToken cancellationToken)
    {
        // Build the backport-conformant Subscription resource
        Subscription subscription = BuildSubscriptionResource(viewDefinitionJson, viewDefinitionName, resourceType);

        // Send through the full MediatR pipeline (validation, handshake, persistence)
        ResourceElement resourceElement = new ResourceElement(subscription.ToTypedElement());
        var request = new CreateResourceRequest(resourceElement, bundleResourceContext: null);

        var response = await SendScopedAsync<UpsertResourceResponse>(request, cancellationToken);

        return response.Outcome.RawResourceElement.Id;
    }

    /// <summary>
    /// Builds a FHIR R4 Subscription resource with the subscriptions-backport profile,
    /// configured for the view-definition-refresh channel type.
    /// </summary>
    internal static Subscription BuildSubscriptionResource(
        string viewDefinitionJson,
        string viewDefinitionName,
        string resourceType)
    {
        var subscription = new Subscription
        {
            Meta = new Meta
            {
                Profile = new List<string> { BackportProfileUrl },
            },
            Status = Subscription.SubscriptionStatus.Requested,
            Reason = $"Auto-created for ViewDefinition '{viewDefinitionName}' materialization",
            End = DateTimeOffset.UtcNow.AddYears(100),
            Criteria = TransactionTopicUrl,
            Channel = new Subscription.ChannelComponent
            {
                // The type is "rest-hook" at the FHIR level; the actual channel is identified
                // by the backport channel type extension below.
                Type = Subscription.SubscriptionChannelType.RestHook,
                Endpoint = $"internal://sqlfhir/{viewDefinitionName}",
                Payload = "application/fhir+json",

                // Carry ViewDefinition metadata as channel headers so they survive
                // persistence and are extracted into ChannelInfo.Properties by the converter.
                Header = new List<string>
                {
                    $"viewDefinitionName: {viewDefinitionName}",
                    $"viewDefinitionJson: {viewDefinitionJson}",
                },
            },
        };

        // Add backport filter criteria extension (resource type filter)
        subscription.CriteriaElement = new FhirString(TransactionTopicUrl);
        subscription.CriteriaElement.Extension.Add(new Extension
        {
            Url = BackportFilterCriteriaUrl,
            Value = new FhirString($"{resourceType}?"),
        });

        // Add backport channel type extension
        subscription.Channel.TypeElement = new Code<Subscription.SubscriptionChannelType>(Subscription.SubscriptionChannelType.RestHook);
        subscription.Channel.TypeElement.Extension.Add(new Extension
        {
            Url = BackportChannelTypeUrl,
            Value = new Coding(ChannelTypeCodingSystem, "view-definition-refresh", "ViewDefinition Refresh"),
        });

        // Add backport payload content extension
        subscription.Channel.PayloadElement = new Code("application/fhir+json");
        subscription.Channel.PayloadElement.Extension.Add(new Extension
        {
            Url = BackportPayloadContentUrl,
            Value = new Code("full-resource"),
        });

        // Add max count extension
        subscription.Channel.Extension.Add(new Extension
        {
            Url = BackportMaxCountUrl,
            Value = new PositiveInt(100),
        });

        return subscription;
    }

    /// <summary>
    /// Sends a MediatR request within a new DI scope, avoiding the "cannot resolve scoped service
    /// from root provider" error when called from singleton services.
    /// </summary>
    private async Task<TResponse> SendScopedAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.Send(request, cancellationToken);
    }

    /// <summary>
    /// Extracts the ViewDefinition JSON from a raw Library resource wrapper.
    /// Used when adopting a ViewDefinition from the database on a node that didn't
    /// originate the registration (multi-node scenario).
    /// </summary>
    private string? ExtractViewDefinitionJsonFromRawResource(ResourceWrapper resource)
    {
        try
        {
            string rawJson = resource.RawResource.Data;
            var parser = new FhirJsonParser();
            Library library = parser.Parse<Library>(rawJson);

            Attachment? content = library.Content.FirstOrDefault(
                c => string.Equals(c.ContentType, ViewDefinitionContentType, StringComparison.OrdinalIgnoreCase));

            if (content?.Data == null || content.Data.Length == 0)
            {
                _logger.LogWarning("Library '{LibraryId}' has no ViewDefinition content attachment", resource.ResourceId);
                return null;
            }

            return System.Text.Encoding.UTF8.GetString(content.Data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract ViewDefinition JSON from Library '{LibraryId}'", resource.ResourceId);
            return null;
        }
    }

    private static (string Name, string ResourceType) ExtractViewDefinitionMetadata(string viewDefinitionJson)
    {
        using JsonDocument doc = JsonDocument.Parse(viewDefinitionJson);
        JsonElement root = doc.RootElement;

        string name = root.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() ?? "unknown" : "unknown";
        string resourceType = root.TryGetProperty("resource", out JsonElement resEl) ? resEl.GetString() ?? "unknown" : "unknown";

        return (name, resourceType);
    }
}
