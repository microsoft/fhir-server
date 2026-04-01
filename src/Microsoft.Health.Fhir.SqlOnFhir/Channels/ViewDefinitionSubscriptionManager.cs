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
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
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
    public async Task<ViewDefinitionRegistration> RegisterAsync(string viewDefinitionJson, CancellationToken cancellationToken)
    {
        return await RegisterAsync(viewDefinitionJson, libraryResourceId: null, cancellationToken);
    }

    /// <summary>
    /// Registers a ViewDefinition for materialization with an optional pre-existing Library resource ID.
    /// When <paramref name="libraryResourceId"/> is provided (e.g., from a Library POST), skips Library creation.
    /// When null, creates a new Library resource to persist the registration.
    /// </summary>
    public async Task<ViewDefinitionRegistration> RegisterAsync(
        string viewDefinitionJson,
        string? libraryResourceId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionJson);

        (string name, string resourceType) = ExtractViewDefinitionMetadata(viewDefinitionJson);

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

            // Step 4: Persist ViewDefinition as a Library resource (if not already provided)
            if (string.IsNullOrEmpty(libraryResourceId))
            {
                libraryResourceId = await CreateLibraryResourceAsync(viewDefinitionJson, name, resourceType, cancellationToken);
            }

            registration.LibraryResourceId = libraryResourceId;

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

        // Delete auto-created Subscription resources
        foreach (string subscriptionId in registration.SubscriptionIds)
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
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionJson);

        (string name, string resourceType) = ExtractViewDefinitionMetadata(viewDefinitionJson);

        var registration = new ViewDefinitionRegistration
        {
            ViewDefinitionJson = viewDefinitionJson,
            ViewDefinitionName = name,
            ResourceType = resourceType,
            LibraryResourceId = libraryResourceId,
            Status = ViewDefinitionStatus.Active,
        };

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

        _logger.LogInformation("Adopted ViewDefinition '{ViewDefName}' into local cache", name);
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
    /// Handles the population complete notification by updating the in-memory registration status.
    /// </summary>
    public Task Handle(ViewDefinitionPopulationCompleteNotification notification, CancellationToken cancellationToken)
    {
        if (_registrations.TryGetValue(notification.ViewDefinitionName, out ViewDefinitionRegistration? registration))
        {
            registration.Status = notification.Success ? ViewDefinitionStatus.Active : ViewDefinitionStatus.Error;
            registration.ErrorMessage = notification.ErrorMessage;

            _logger.LogInformation(
                "ViewDefinition '{ViewDefName}' population complete: {Status} ({Rows} rows)",
                notification.ViewDefinitionName,
                registration.Status,
                notification.RowsInserted);
        }

        return Task.CompletedTask;
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
    /// Creates a FHIR Library resource that wraps the ViewDefinition JSON for persistent storage.
    /// The Library is tagged with the ViewDefinition profile so it can be discovered on startup.
    /// </summary>
    private async Task<string> CreateLibraryResourceAsync(
        string viewDefinitionJson,
        string viewDefinitionName,
        string resourceType,
        CancellationToken cancellationToken)
    {
        var library = new Library
        {
            Meta = new Meta
            {
                Profile = new List<string> { ViewDefinitionLibraryProfile },
            },
            Name = viewDefinitionName,
            Title = $"ViewDefinition: {viewDefinitionName}",
            Status = PublicationStatus.Active,
            Type = new CodeableConcept("http://terminology.hl7.org/CodeSystem/library-type", "logic-library"),
            Description = new Markdown($"SQL on FHIR v2 ViewDefinition for {resourceType} resources. Auto-created by materialization registration."),
            Content = new List<Attachment>
            {
                new Attachment
                {
                    ContentType = ViewDefinitionContentType,
                    Data = System.Text.Encoding.UTF8.GetBytes(viewDefinitionJson),
                },
            },
        };

        ResourceElement resourceElement = new ResourceElement(library.ToTypedElement());
        var request = new CreateResourceRequest(resourceElement, bundleResourceContext: null);
        var response = await SendScopedAsync<UpsertResourceResponse>(request, cancellationToken);

        string libraryId = response.Outcome.RawResourceElement.Id;
        _logger.LogInformation(
            "Created Library resource '{LibraryId}' for ViewDefinition '{ViewDefName}'",
            libraryId,
            viewDefinitionName);

        return libraryId;
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

    private static (string Name, string ResourceType) ExtractViewDefinitionMetadata(string viewDefinitionJson)
    {
        using JsonDocument doc = JsonDocument.Parse(viewDefinitionJson);
        JsonElement root = doc.RootElement;

        string name = root.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() ?? "unknown" : "unknown";
        string resourceType = root.TryGetProperty("resource", out JsonElement resEl) ? resEl.GetString() ?? "unknown" : "unknown";

        return (name, resourceType);
    }
}
