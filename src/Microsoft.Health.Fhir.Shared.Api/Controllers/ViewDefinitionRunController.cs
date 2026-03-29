// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers;

/// <summary>
/// Controller for the SQL on FHIR $viewdefinition-run operation.
/// Evaluates a ViewDefinition and returns tabular results in the requested format.
/// </summary>
[ServiceFilter(typeof(AuditLoggingFilterAttribute))]
[ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
public class ViewDefinitionRunController : Controller
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionRunController"/> class.
    /// </summary>
    /// <param name="mediator">The MediatR mediator.</param>
    public ViewDefinitionRunController(IMediator mediator)
    {
        _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
    }

    /// <summary>
    /// POST ViewDefinition/$run — Type-level invocation with inline ViewDefinition.
    /// </summary>
    [HttpPost]
    [Route(KnownRoutes.ViewDefinitionRun)]
    [AuditEventType(AuditEventSubType.Read)]
    public async Task<IActionResult> RunPost([FromBody] Parameters parameters, [FromQuery(Name = "_format")] string? format)
    {
        string? viewDefinitionJson = null;

        // Extract inline ViewDefinition from Parameters resource
        var viewResourceParam = parameters?.Parameter?.Find(p =>
            string.Equals(p.Name, "viewResource", StringComparison.OrdinalIgnoreCase));

        if (viewResourceParam?.Resource != null)
        {
            viewDefinitionJson = JsonSerializer.Serialize(
                viewResourceParam.Resource,
                new JsonSerializerOptions { WriteIndented = false });
        }

        // Also check for viewDefinitionJson as a string parameter
        var viewJsonParam = parameters?.Parameter?.Find(p =>
            string.Equals(p.Name, "viewDefinitionJson", StringComparison.OrdinalIgnoreCase));

        if (viewJsonParam?.Value is FhirString fhirString)
        {
            viewDefinitionJson = fhirString.Value;
        }

        int? limit = null;
        var limitParam = parameters?.Parameter?.Find(p =>
            string.Equals(p.Name, "_limit", StringComparison.OrdinalIgnoreCase));

        if (limitParam?.Value is Integer limitInt)
        {
            limit = limitInt.Value;
        }

        var request = new ViewDefinitionRunRequest(
            viewDefinitionJson: viewDefinitionJson,
            format: format ?? "json",
            limit: limit);

        return await ExecuteAsync(request);
    }

    /// <summary>
    /// GET ViewDefinition/{id}/$run — Instance-level invocation for a registered ViewDefinition.
    /// </summary>
    [HttpGet]
    [Route(KnownRoutes.ViewDefinitionRunById)]
    [AuditEventType(AuditEventSubType.Read)]
    public async Task<IActionResult> RunById(
        [FromRoute] string id,
        [FromQuery(Name = "_format")] string? format,
        [FromQuery(Name = "_limit")] int? limit)
    {
        var request = new ViewDefinitionRunRequest(
            viewDefinitionName: id,
            format: format ?? "json",
            limit: limit);

        return await ExecuteAsync(request);
    }

    private async Task<IActionResult> ExecuteAsync(ViewDefinitionRunRequest request)
    {
        ViewDefinitionRunResponse response = await _mediator.Send(request, HttpContext.RequestAborted);

        return new ContentResult
        {
            Content = response.FormattedOutput,
            ContentType = response.ContentType,
            StatusCode = 200,
        };
    }
}
