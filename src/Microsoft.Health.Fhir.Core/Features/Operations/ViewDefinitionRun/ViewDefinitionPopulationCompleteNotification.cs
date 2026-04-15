// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;

/// <summary>
/// MediatR notification published when a ViewDefinition population job completes.
/// The subscription manager listens for this to update the in-memory registration status.
/// </summary>
public class ViewDefinitionPopulationCompleteNotification : INotification
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionPopulationCompleteNotification"/> class.
    /// </summary>
    /// <param name="viewDefinitionName">The ViewDefinition name.</param>
    /// <param name="success">Whether the population completed successfully.</param>
    /// <param name="rowsInserted">Total rows inserted.</param>
    /// <param name="errorMessage">Error message if failed.</param>
    /// <param name="libraryResourceId">The Library resource ID for persisting status across nodes.</param>
    public ViewDefinitionPopulationCompleteNotification(
        string viewDefinitionName,
        bool success,
        long rowsInserted = 0,
        string errorMessage = null,
        string libraryResourceId = null)
    {
        ViewDefinitionName = viewDefinitionName;
        Success = success;
        RowsInserted = rowsInserted;
        ErrorMessage = errorMessage;
        LibraryResourceId = libraryResourceId;
    }

    /// <summary>
    /// Gets the ViewDefinition name.
    /// </summary>
    public string ViewDefinitionName { get; }

    /// <summary>
    /// Gets a value indicating whether population succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets the total rows inserted.
    /// </summary>
    public long RowsInserted { get; }

    /// <summary>
    /// Gets the error message if population failed.
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// Gets the Library resource ID so the handler can persist status even on nodes
    /// that did not originate the registration.
    /// </summary>
    public string LibraryResourceId { get; }
}
