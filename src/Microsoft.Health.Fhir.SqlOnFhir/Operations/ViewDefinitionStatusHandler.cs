// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;
using Microsoft.Health.Fhir.SqlOnFhir.Channels;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;

namespace Microsoft.Health.Fhir.SqlOnFhir.Operations;

/// <summary>
/// Handles ViewDefinition status queries by reading the in-memory registration state
/// and verifying the materialized SQL table exists.
/// </summary>
public sealed class ViewDefinitionStatusHandler : IRequestHandler<ViewDefinitionStatusRequest, ViewDefinitionStatusResponse>
{
    private readonly IViewDefinitionSubscriptionManager _subscriptionManager;
    private readonly IViewDefinitionSchemaManager _schemaManager;
    private readonly ILogger<ViewDefinitionStatusHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionStatusHandler"/> class.
    /// </summary>
    public ViewDefinitionStatusHandler(
        IViewDefinitionSubscriptionManager subscriptionManager,
        IViewDefinitionSchemaManager schemaManager,
        ILogger<ViewDefinitionStatusHandler> logger)
    {
        _subscriptionManager = subscriptionManager;
        _schemaManager = schemaManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ViewDefinitionStatusResponse> Handle(
        ViewDefinitionStatusRequest request,
        CancellationToken cancellationToken)
    {
        ViewDefinitionRegistration? registration = _subscriptionManager.GetRegistration(request.ViewDefinitionName);

        if (registration == null)
        {
            return new ViewDefinitionStatusResponse
            {
                ViewDefinitionName = request.ViewDefinitionName,
                Status = "NotFound",
            };
        }

        // Only check SQL table existence when the target includes SqlServer.
        // Fabric/Parquet targets don't use SQL tables.
        bool tableExists = registration.Target.HasFlag(MaterializationTarget.SqlServer)
            && await _schemaManager.TableExistsAsync(request.ViewDefinitionName, cancellationToken);

        return new ViewDefinitionStatusResponse
        {
            ViewDefinitionName = registration.ViewDefinitionName,
            ResourceType = registration.ResourceType,
            Status = registration.Status.ToString(),
            ErrorMessage = registration.ErrorMessage,
            SubscriptionIds = registration.SubscriptionIds.ToList(),
            LibraryResourceId = registration.LibraryResourceId,
            RegisteredAt = registration.RegisteredAt,
            TableExists = tableExists,
            Target = registration.Target.ToString(),
        };
    }
}
