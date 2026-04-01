// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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

#nullable enable

/// <summary>
/// Controller for the SQL on FHIR $viewdefinition-run and $viewdefinition-export operations.
/// Evaluates a ViewDefinition and returns tabular results in the requested format.
/// </summary>
[ServiceFilter(typeof(AuditLoggingFilterAttribute))]
[ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
public class ViewDefinitionRunController : Controller
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new() { WriteIndented = false };

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
                CompactJsonOptions);
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
        [FromRoute(Name = KnownActionParameterNames.Id)] string id,
        [FromQuery(Name = "_format")] string? format,
        [FromQuery(Name = "_limit")] int? limit)
    {
        var request = new ViewDefinitionRunRequest(
            viewDefinitionName: id,
            format: format ?? "json",
            limit: limit);

        return await ExecuteAsync(request);
    }

    /// <summary>
    /// GET ViewDefinition — Lists all registered ViewDefinitions and their materialization status.
    /// </summary>
    [HttpGet]
    [Route(KnownRoutes.ViewDefinitionList)]
    [AuditEventType(AuditEventSubType.Read)]
    public async Task<IActionResult> List()
    {
        var request = new ViewDefinitionListRequest();
        ViewDefinitionListResponse response = await _mediator.Send(request, HttpContext.RequestAborted);

        return new JsonResult(response) { StatusCode = 200 };
    }

    /// <summary>
    /// GET ViewDefinition/{id} — Returns the materialization status of a registered ViewDefinition.
    /// Clients use this to track progress from Created → Populating → Active.
    /// The URL is returned as a Content-Location header when a ViewDefinition Library is POSTed.
    /// </summary>
    [HttpGet]
    [Route(KnownRoutes.ViewDefinitionStatus)]
    [AuditEventType(AuditEventSubType.Read)]
    public async Task<IActionResult> GetStatus([FromRoute(Name = KnownActionParameterNames.Id)] string id)
    {
        var request = new ViewDefinitionStatusRequest(id);
        ViewDefinitionStatusResponse response = await _mediator.Send(request, HttpContext.RequestAborted);

        return new JsonResult(response) { StatusCode = response.Status == "NotFound" ? 404 : 200 };
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

    /// <summary>
    /// POST ViewDefinition/$viewdefinition-export — Bulk export of ViewDefinition results.
    /// If the ViewDefinition is already materialized in the requested format, returns download URLs immediately.
    /// Otherwise enqueues an async export job and returns 202 Accepted with a status polling URL.
    /// </summary>
    [HttpPost]
    [Route(KnownRoutes.ViewDefinitionExport)]
    [AuditEventType(AuditEventSubType.Export)]
    public async Task<IActionResult> Export([FromBody] Parameters parameters, [FromQuery(Name = "_format")] string? format)
    {
        string? viewDefinitionJson = null;
        string? viewDefinitionName = null;

        if (parameters?.Parameter != null)
        {
            // Extract view.viewResource (inline ViewDefinition)
            var viewParam = parameters.Parameter.Find(p =>
                string.Equals(p.Name, "view", StringComparison.OrdinalIgnoreCase));

            if (viewParam?.Part != null)
            {
                var viewResourcePart = viewParam.Part.Find(p =>
                    string.Equals(p.Name, "viewResource", StringComparison.OrdinalIgnoreCase));

                if (viewResourcePart?.Resource != null)
                {
                    viewDefinitionJson = JsonSerializer.Serialize(
                        viewResourcePart.Resource,
                        CompactJsonOptions);
                }

                var viewRefPart = viewParam.Part.Find(p =>
                    string.Equals(p.Name, "viewReference", StringComparison.OrdinalIgnoreCase));

                if (viewRefPart?.Value is ResourceReference viewRef)
                {
                    viewDefinitionName = viewRef.Reference?.Split('/').LastOrDefault();
                }

                var namePart = viewParam.Part.Find(p =>
                    string.Equals(p.Name, "name", StringComparison.OrdinalIgnoreCase));

                if (namePart?.Value is FhirString nameStr)
                {
                    viewDefinitionName = nameStr.Value;
                }
            }

            // Also check for simple viewDefinitionJson string parameter (convenience)
            var jsonParam = parameters.Parameter.Find(p =>
                string.Equals(p.Name, "viewDefinitionJson", StringComparison.OrdinalIgnoreCase));

            if (jsonParam?.Value is FhirString jsonStr)
            {
                viewDefinitionJson = jsonStr.Value;
            }

            var nameParam = parameters.Parameter.Find(p =>
                string.Equals(p.Name, "viewDefinitionName", StringComparison.OrdinalIgnoreCase));

            if (nameParam?.Value is FhirString nameString)
            {
                viewDefinitionName = nameString.Value;
            }
        }

        var request = new ViewDefinitionExportRequest(
            viewDefinitionJson: viewDefinitionJson,
            viewDefinitionName: viewDefinitionName,
            format: format ?? "ndjson");

        ViewDefinitionExportResponse response = await _mediator.Send(request, HttpContext.RequestAborted);

        if (response.IsComplete)
        {
            // Fast path — already materialized, return output URLs
            var resultParams = new Parameters();

            foreach (var output in response.Outputs)
            {
                var outputParam = new Parameters.ParameterComponent { Name = "output" };
                outputParam.Part.Add(new Parameters.ParameterComponent { Name = "name", Value = new FhirString(output.Name) });
                outputParam.Part.Add(new Parameters.ParameterComponent { Name = "location", Value = new FhirUrl(output.Location) });
                outputParam.Part.Add(new Parameters.ParameterComponent { Name = "format", Value = new Code(output.Format) });
                resultParams.Parameter.Add(outputParam);
            }

            return new ObjectResult(resultParams) { StatusCode = 200 };
        }

        // Async path — job enqueued, return 202 with status URL
        Response.Headers["Content-Location"] = response.StatusUrl;

        var statusParams = new Parameters();
        statusParams.Parameter.Add(new Parameters.ParameterComponent
        {
            Name = "exportId",
            Value = new FhirString(response.ExportId),
        });
        statusParams.Parameter.Add(new Parameters.ParameterComponent
        {
            Name = "status",
            Value = new Code("accepted"),
        });

        return new ObjectResult(statusParams) { StatusCode = 202 };
    }
}
