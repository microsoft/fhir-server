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

        // Check if this Library is a ViewDefinition wrapper by matching registration
        string libraryId = request.ResourceKey.Id;
        ViewDefinitionRegistration? registration = FindRegistrationByLibraryId(libraryId);

        // Let the delete proceed first
        DeleteResourceResponse response = await next(cancellationToken);

        // If this was a ViewDefinition Library, clean up the materialized table
        if (registration != null)
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
        else
        {
            _logger.LogDebug("Library '{LibraryId}' deleted but no matching ViewDefinition registration found", libraryId);
        }

        return response;
    }

    private ViewDefinitionRegistration? FindRegistrationByLibraryId(string libraryId)
    {
        // Match by Library resource ID
        ViewDefinitionRegistration? match = _subscriptionManager.GetAllRegistrations()
            .FirstOrDefault(r => string.Equals(r.LibraryResourceId, libraryId, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            return match;
        }

        // Fallback: match any registration that doesn't have a LibraryResourceId set
        // (can happen if registration errored after Library was created but before ID was saved)
        // In this case, we can't be sure which registration this Library belongs to,
        // so we skip cleanup and let the sync service handle it on next poll.
        return null;
    }
}
