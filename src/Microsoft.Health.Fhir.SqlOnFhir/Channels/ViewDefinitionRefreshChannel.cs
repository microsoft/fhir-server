// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Microsoft.Health.Fhir.Subscriptions.Channels;
using Microsoft.Health.Fhir.Subscriptions.Models;

namespace Microsoft.Health.Fhir.SqlOnFhir.Channels;

/// <summary>
/// Subscription channel that materializes ViewDefinition rows when FHIR resources change.
/// When a subscription fires, this channel re-evaluates the associated ViewDefinition(s) against
/// the changed resources and performs incremental upserts into the materialized SQL tables.
/// </summary>
[ChannelType(SubscriptionChannelType.ViewDefinitionRefresh)]
public sealed class ViewDefinitionRefreshChannel : ISubscriptionChannel
{
    private readonly IViewDefinitionMaterializer _materializer;
    private readonly IResourceDeserializer _resourceDeserializer;
    private readonly ILogger<ViewDefinitionRefreshChannel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionRefreshChannel"/> class.
    /// </summary>
    /// <param name="materializer">The materializer for upserting rows into SQL tables.</param>
    /// <param name="resourceDeserializer">Deserializer for converting ResourceWrapper to ResourceElement.</param>
    /// <param name="logger">The logger instance.</param>
    public ViewDefinitionRefreshChannel(
        IViewDefinitionMaterializer materializer,
        IResourceDeserializer resourceDeserializer,
        ILogger<ViewDefinitionRefreshChannel> logger)
    {
        _materializer = materializer;
        _resourceDeserializer = resourceDeserializer;
        _logger = logger;
    }

    /// <summary>
    /// Processes changed resources by re-evaluating the ViewDefinition and upserting rows.
    /// The ViewDefinition JSON and name are stored in the subscription's channel properties:
    /// <list type="bullet">
    ///   <item><c>viewDefinitionJson</c> — the full ViewDefinition JSON</item>
    ///   <item><c>viewDefinitionName</c> — the ViewDefinition name (SQL table name)</item>
    /// </list>
    /// </summary>
    public async Task PublishAsync(
        IReadOnlyCollection<ResourceWrapper> resources,
        SubscriptionInfo subscriptionInfo,
        DateTimeOffset transactionTime,
        CancellationToken cancellationToken)
    {
        if (!TryGetViewDefinitionProperties(subscriptionInfo, out string? viewDefJson, out string? viewDefName))
        {
            _logger.LogWarning(
                "ViewDefinitionRefreshChannel received notification but subscription '{SubscriptionId}' " +
                "is missing viewDefinitionJson or viewDefinitionName channel properties",
                subscriptionInfo.ResourceId);
            return;
        }

        _logger.LogDebug(
            "ViewDefinitionRefreshChannel processing {ResourceCount} resource(s) for ViewDefinition '{ViewDefName}'",
            resources.Count,
            viewDefName);

        int totalRowsUpserted = 0;
        int failedResources = 0;

        foreach (ResourceWrapper wrapper in resources)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                string resourceKey = $"{wrapper.ResourceTypeName}/{wrapper.ResourceId}";

                if (wrapper.IsDeleted)
                {
                    await _materializer.DeleteResourceAsync(viewDefName!, resourceKey, cancellationToken);

                    _logger.LogDebug(
                        "Deleted rows for deleted resource '{ResourceKey}' from '{ViewDefName}'",
                        resourceKey,
                        viewDefName);
                }
                else
                {
                    var resourceElement = _resourceDeserializer.Deserialize(wrapper);

                    int rowsInserted = await _materializer.UpsertResourceAsync(
                        viewDefJson!,
                        viewDefName!,
                        resourceElement,
                        resourceKey,
                        cancellationToken);

                    totalRowsUpserted += rowsInserted;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedResources++;
                _logger.LogWarning(
                    ex,
                    "Failed to materialize resource {ResourceType}/{ResourceId} for ViewDefinition '{ViewDefName}'",
                    wrapper.ResourceTypeName,
                    wrapper.ResourceId,
                    viewDefName);
            }
        }

        _logger.LogInformation(
            "ViewDefinitionRefreshChannel completed for '{ViewDefName}': {RowsUpserted} rows upserted, {Failures} failures",
            viewDefName,
            totalRowsUpserted,
            failedResources);
    }

    /// <summary>
    /// Validates that the subscription endpoint and ViewDefinition properties are present.
    /// </summary>
    public Task PublishHandShakeAsync(SubscriptionInfo subscriptionInfo, CancellationToken cancellationToken)
    {
        if (!TryGetViewDefinitionProperties(subscriptionInfo, out _, out _))
        {
            throw new Subscriptions.Validation.SubscriptionException(
                "ViewDefinitionRefresh channel requires 'viewDefinitionJson' and 'viewDefinitionName' properties.");
        }

        _logger.LogInformation(
            "ViewDefinitionRefreshChannel handshake succeeded for subscription '{SubscriptionId}'",
            subscriptionInfo.ResourceId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Heartbeat is a no-op for the ViewDefinition refresh channel.
    /// </summary>
    public Task PublishHeartBeatAsync(SubscriptionInfo subscriptionInfo, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Extracts ViewDefinition properties from the subscription's channel properties.
    /// </summary>
    private static bool TryGetViewDefinitionProperties(
        SubscriptionInfo subscriptionInfo,
        out string? viewDefinitionJson,
        out string? viewDefinitionName)
    {
        viewDefinitionJson = null;
        viewDefinitionName = null;

        var properties = subscriptionInfo.Channel.Properties;
        if (properties == null)
        {
            return false;
        }

        properties.TryGetValue("viewDefinitionJson", out viewDefinitionJson);
        properties.TryGetValue("viewDefinitionName", out viewDefinitionName);

        return !string.IsNullOrWhiteSpace(viewDefinitionJson) && !string.IsNullOrWhiteSpace(viewDefinitionName);
    }
}
