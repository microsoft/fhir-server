// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;

namespace Microsoft.Health.Fhir.SqlOnFhir.Channels;

/// <summary>
/// MediatR pipeline behavior that intercepts deletion of Library resources containing ViewDefinitions.
/// When a Library resource tagged with the ViewDefinition profile is deleted, this behavior triggers
/// cleanup of the materialized SQL table and auto-created Subscription resources via
/// <see cref="IViewDefinitionSubscriptionManager.UnregisterAsync"/>.
/// </summary>
public sealed class ViewDefinitionLibraryCleanupBehavior : IPipelineBehavior<DeleteResourceRequest, DeleteResourceResponse>
{
    private readonly IViewDefinitionSubscriptionManager _subscriptionManager;
    private readonly ILogger<ViewDefinitionLibraryCleanupBehavior> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionLibraryCleanupBehavior"/> class.
    /// </summary>
    /// <param name="subscriptionManager">The ViewDefinition subscription manager for cleanup.</param>
    /// <param name="logger">The logger instance.</param>
    public ViewDefinitionLibraryCleanupBehavior(
        IViewDefinitionSubscriptionManager subscriptionManager,
        ILogger<ViewDefinitionLibraryCleanupBehavior> logger)
    {
        _subscriptionManager = subscriptionManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DeleteResourceResponse> Handle(
        DeleteResourceRequest request,
        RequestHandlerDelegate<DeleteResourceResponse> next,
        CancellationToken cancellationToken)
    {
        // Only intercept Library resource deletions
        if (!string.Equals(request.ResourceKey.ResourceType, "Library", StringComparison.OrdinalIgnoreCase))
        {
            return await next(cancellationToken);
        }

        // Try to find a matching ViewDefinition registration before the delete proceeds
        string libraryId = request.ResourceKey.Id;
        ViewDefinitionRegistration? registration = FindRegistrationByLibraryId(libraryId);

        // If not found by ID, check all registrations — the Library `name` field matches
        // the ViewDefinition name, so we can search by iterating registrations and checking
        // if any has a matching name but missing/different LibraryResourceId
        string? viewDefNameToClean = registration?.ViewDefinitionName;

        // Let the delete proceed
        DeleteResourceResponse response = await next(cancellationToken);

        // If we found a registration, clean up
        if (registration != null)
        {
            await CleanupRegistrationAsync(libraryId, registration, cancellationToken);
        }
        else
        {
            // Fallback: try to match by scanning all registrations for any without a LibraryResourceId.
            // This handles the case where registration errored after Library creation but before
            // the Library ID was saved on the registration.
            foreach (ViewDefinitionRegistration reg in _subscriptionManager.GetAllRegistrations())
            {
                if (string.IsNullOrEmpty(reg.LibraryResourceId) || string.Equals(reg.LibraryResourceId, libraryId, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "Library '{LibraryId}' deleted. Found orphaned registration '{ViewDefName}' to clean up",
                        libraryId,
                        reg.ViewDefinitionName);
                    await CleanupRegistrationAsync(libraryId, reg, cancellationToken);
                    break;
                }
            }
        }

        return response;
    }

    private async Task CleanupRegistrationAsync(
        string libraryId,
        ViewDefinitionRegistration registration,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Library '{LibraryId}' deleted for ViewDef '{ViewDefName}'. Dropping table and subscriptions",
            libraryId,
            registration.ViewDefinitionName);

        try
        {
            // Clear the LibraryResourceId to prevent UnregisterAsync from trying to re-delete
            // the Library we're already in the process of deleting
            registration.LibraryResourceId = null;

            await _subscriptionManager.UnregisterAsync(
                registration.ViewDefinitionName,
                dropTable: true,
                cancellationToken);
        }
        catch (Exception ex)
        {
            string message = "Failed to clean up materialized resources for ViewDefinition after Library deletion";
            _logger.LogWarning(ex, "{Message}: {ViewDefName}", message, registration.ViewDefinitionName);
        }
    }

    private ViewDefinitionRegistration? FindRegistrationByLibraryId(string libraryId)
    {
        // Match by Library resource ID
        ViewDefinitionRegistration? match = _subscriptionManager.GetAllRegistrations()
            .FirstOrDefault(r => string.Equals(r.LibraryResourceId, libraryId, StringComparison.OrdinalIgnoreCase));

        return match;
    }

    /// <summary>
    /// Finds a registration by ViewDefinition name. Used as a fallback when the Library resource ID
    /// wasn't recorded on the registration (e.g., registration errored after Library creation).
    /// </summary>
    private ViewDefinitionRegistration? FindRegistrationByName(string viewDefinitionName)
    {
        return _subscriptionManager.GetRegistration(viewDefinitionName);
    }
}
