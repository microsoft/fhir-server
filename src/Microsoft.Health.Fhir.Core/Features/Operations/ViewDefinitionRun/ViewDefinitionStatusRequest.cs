// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;

/// <summary>
/// MediatR request for querying the materialization status of a registered ViewDefinition.
/// </summary>
public class ViewDefinitionStatusRequest : IRequest<ViewDefinitionStatusResponse>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionStatusRequest"/> class.
    /// </summary>
    /// <param name="viewDefinitionName">The ViewDefinition name.</param>
    public ViewDefinitionStatusRequest(string viewDefinitionName)
    {
        ViewDefinitionName = viewDefinitionName;
    }

    /// <summary>
    /// Gets the ViewDefinition name.
    /// </summary>
    public string ViewDefinitionName { get; }
}
