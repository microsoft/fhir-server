// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;

/// <summary>
/// MediatR request for listing all registered ViewDefinitions and their materialization status.
/// </summary>
public class ViewDefinitionListRequest : IRequest<ViewDefinitionListResponse>
{
}
