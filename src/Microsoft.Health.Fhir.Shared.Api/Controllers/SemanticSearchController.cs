// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.SemanticSearch;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    /// <summary>
    /// Provides semantic search over resources in a patient compartment.
    /// </summary>
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ServiceFilter(typeof(ValidateFormatParametersAttribute))]
    [ValidateModelState]
    public sealed class SemanticSearchController : Controller
    {
        private static readonly HashSet<string> SupportedResourceTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            ResourceType.DocumentReference.ToString(),
            ResourceType.Observation.ToString(),
            ResourceType.DiagnosticReport.ToString(),
        };

        private readonly IMediator _mediator;
        private readonly VectorSearchQueryConfiguration _queryConfiguration;

        /// <summary>
        /// Initializes a new instance of the <see cref="SemanticSearchController"/> class.
        /// </summary>
        /// <param name="mediator">The mediator used to dispatch the semantic-search request.</param>
        /// <param name="configuration">The vector-search configuration.</param>
        public SemanticSearchController(IMediator mediator, IOptions<VectorSearchConfiguration> configuration)
        {
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
            _queryConfiguration = EnsureArg.IsNotNull(configuration, nameof(configuration)).Value.Query;
        }

        /// <summary>
        /// Semantically searches resources associated with a patient.
        /// </summary>
        /// <param name="idParameter">The patient resource ID.</param>
        /// <param name="parameters">The semantic-search operation parameters.</param>
        [HttpPost]
        [Route(KnownRoutes.SemanticSearchPatientById)]
        [AuditEventType(AuditEventSubType.SearchSystem)]
        public async Task<IActionResult> Search(string idParameter, [FromBody] Parameters parameters)
        {
            FhirString query = parameters?.Parameter?.FirstOrDefault(parameter => parameter.Name == "query")?.Value as FhirString;
            Integer count = parameters?.Parameter?.FirstOrDefault(parameter => parameter.Name == "count")?.Value as Integer;
            IReadOnlyList<string> resourceTypes = parameters?.Parameter?
                .Where(parameter => parameter.Name == "type")
                .Select(parameter => (parameter.Value as Code)?.Value)
                .Where(resourceType => !string.IsNullOrWhiteSpace(resourceType))
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? new List<string>();

            if (string.IsNullOrWhiteSpace(query?.Value))
            {
                throw new RequestNotValidException("Semantic search requires a query parameter.");
            }

            if (count?.Value <= 0)
            {
                throw new RequestNotValidException("Semantic search count must be greater than zero.");
            }

            if (count?.Value > _queryConfiguration.MaxCount)
            {
                throw new RequestNotValidException($"Semantic search count must not exceed {_queryConfiguration.MaxCount}.");
            }

            string unsupportedResourceType = resourceTypes.FirstOrDefault(resourceType => !SupportedResourceTypes.Contains(resourceType));
            if (unsupportedResourceType != null)
            {
                throw new RequestNotValidException($"Resource type '{unsupportedResourceType}' is not supported by patient semantic search.");
            }

            SemanticSearchResponse response = await _mediator.Send(
                new SemanticSearchRequest(query.Value, $"Patient/{idParameter}", count?.Value ?? _queryConfiguration.DefaultCount, resourceTypes),
                HttpContext.RequestAborted);
            return FhirResult.Create(response.Bundle);
        }
    }
}
