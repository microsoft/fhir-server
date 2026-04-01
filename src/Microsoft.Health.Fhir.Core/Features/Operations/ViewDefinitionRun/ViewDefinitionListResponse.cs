// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;

/// <summary>
/// Response containing all registered ViewDefinition statuses.
/// </summary>
public class ViewDefinitionListResponse
{
    /// <summary>
    /// Gets the list of ViewDefinition statuses.
    /// </summary>
    public Collection<ViewDefinitionStatusResponse> ViewDefinitions { get; } = new();
}
