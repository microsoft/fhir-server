// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;
using Microsoft.Health.Fhir.SqlOnFhir.Channels;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;

namespace Microsoft.Health.Fhir.SqlOnFhir.Operations;

/// <summary>
/// Handles listing all registered ViewDefinitions and their materialization status.
/// </summary>
public sealed class ViewDefinitionListHandler : IRequestHandler<ViewDefinitionListRequest, ViewDefinitionListResponse>
{
    private readonly IViewDefinitionSubscriptionManager _subscriptionManager;
    private readonly IViewDefinitionSchemaManager _schemaManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionListHandler"/> class.
    /// </summary>
    public ViewDefinitionListHandler(
        IViewDefinitionSubscriptionManager subscriptionManager,
        IViewDefinitionSchemaManager schemaManager)
    {
        _subscriptionManager = subscriptionManager;
        _schemaManager = schemaManager;
    }

    /// <inheritdoc />
    public async Task<ViewDefinitionListResponse> Handle(
        ViewDefinitionListRequest request,
        CancellationToken cancellationToken)
    {
        var response = new ViewDefinitionListResponse();

        foreach (ViewDefinitionRegistration registration in _subscriptionManager.GetAllRegistrations())
        {
            // Only check SQL table existence when the target includes SqlServer.
            // Fabric/Parquet targets don't use SQL tables.
            bool tableExists = registration.Target.HasFlag(MaterializationTarget.SqlServer)
                && await _schemaManager.TableExistsAsync(registration.ViewDefinitionName, cancellationToken);

            response.ViewDefinitions.Add(new ViewDefinitionStatusResponse
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
            });
        }

        return response;
    }
}
