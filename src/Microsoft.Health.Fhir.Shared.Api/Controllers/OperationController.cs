// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Operations;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Api.Features.Routing.Operations;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Routing;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ServiceFilter(typeof(ValidateContentTypeFilterAttribute))]
    [ValidateResourceTypeFilter(true)]
    [ValidateModelState]
    [AllowAnonymous]
    public class OperationController : Controller
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IMediator _mediator;
        private readonly OperationRequestContent _operationRequestBody;
        private readonly IFhirAuthorizationService _authorizationService;
        private readonly ILogger<OperationController> _logger;

        public OperationController(
            IServiceProvider serviceProvider,
            IMediator mediator,
            OperationRequestContent operationRequestBody,
            IFhirAuthorizationService authorizationService,
            ILogger<OperationController> logger)
        {
            _serviceProvider = serviceProvider;
            _mediator = mediator;
            _operationRequestBody = operationRequestBody;
            _authorizationService = authorizationService;
            _logger = logger;
        }

        [Route(KnownRoutes.Operation, Name = "OperationRoot")]
        [AuditEventType(AuditEventSubType.Operation)]
        public async Task<IActionResult> ProcessOperationRequestRootLevel(string operationParameter, [FromBody, ModelBinder(BinderType = typeof(OptionalBodyBinder))] Resource resource)
        {
            return await ProcessRequest(operationParameter, resource);
        }

        [Route("{segment1}" + KnownRoutes.Operation, Name = "Operation1Segment")]
        [AuditEventType(AuditEventSubType.Operation)]
        public async Task<IActionResult> ProcessOperationRequest1Level(string operationParameter, [FromBody, ModelBinder(BinderType = typeof(OptionalBodyBinder))] Resource resource = null)
        {
            return await ProcessRequest(operationParameter, resource);
        }

        [Route("{segment1}/{segment2}" + KnownRoutes.Operation, Name = "Operation2Segment")]
        [AuditEventType(AuditEventSubType.Operation)]
        public async Task<IActionResult> ProcessOperationRequest2Level(string operationParameter, [FromBody, ModelBinder(BinderType = typeof(OptionalBodyBinder))] Resource resource = null)
        {
            return await ProcessRequest(operationParameter, resource);
        }

        [Route("{segment1}/{segment2}/{segment3}" + KnownRoutes.Operation, Name = "Operation3Segment")]
        [AuditEventType(AuditEventSubType.Operation)]
        public async Task<IActionResult> ProcessOperationRequest3Level(string operationParameter, [FromBody, ModelBinder(BinderType = typeof(OptionalBodyBinder))] Resource resource = null)
        {
            return await ProcessRequest(operationParameter, resource);
        }

        [Route("{segment1}/{segment2}/{segment3}/{segment4}" + KnownRoutes.Operation, Name = "Operation4Segment")]
        [AuditEventType(AuditEventSubType.Operation)]
        public async Task<IActionResult> ProcessOperationRequest4Level(string operationParameter, [FromBody, ModelBinder(BinderType = typeof(OptionalBodyBinder))] Resource resource = null)
        {
            return await ProcessRequest(operationParameter, resource);
        }

        private async Task<IActionResult> ProcessRequest(string operationParameter, Resource resource)
        {
            var operationType = OperationRegistry.FindOperation("$" + operationParameter, Request.Method);

            if (operationType == null)
            {
                return NotFound();
            }

            var attribute = operationType.GetCustomAttribute<OperationAttribute>();
            if (!attribute.AllowAnonymous)
            {
                await _authorizationService.CheckAccess(attribute.DataActions);
            }

            if (resource != null)
            {
                _operationRequestBody.Resource = resource.ToResourceElement();
            }

            var operationRequest = (IOperationRequest)_serviceProvider.GetService(operationType);
            var resultType = operationType.GetInterfaces().FirstOrDefault(x => x.IsGenericType && typeof(IOperationResponse).IsAssignableFrom(x.GenericTypeArguments.First()))?.GenericTypeArguments.First();

            Debug.Assert(resultType != null, "ResultType does not have an IRequest<IOperationResponse> interface.");

            // Bind message properties
            foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(operationRequest))
            {
                if (Request.Query.ContainsKey(property.Name) && !property.IsReadOnly)
                {
                    try
                    {
                        property.SetValue(operationRequest, Request.Query[property.Name]);
                    }
                    catch (InvalidCastException ex)
                    {
                        _logger.LogDebug(ex, $"Unable to set Operation message property: {property.Name} from query");
                    }
                }
            }

            MethodInfo executionFunction = GetType().GetMethod(nameof(SendOperation), BindingFlags.Instance | BindingFlags.NonPublic)
                .MakeGenericMethod(operationType, resultType);

            IActionResult result = await (Task<IActionResult>)executionFunction.Invoke(this, new object[] { operationRequest });

            return result;
        }

        private async Task<IActionResult> SendOperation<TRequest, TResponse>(TRequest request)
            where TRequest : IRequest<TResponse>, IOperationRequest
            where TResponse : IOperationResponse
        {
            IRequest<TResponse> outgoingRequest = request;

            TResponse response = await _mediator.Send(outgoingRequest, HttpContext.RequestAborted);

            if (response is IOperationFhirResponse fhirResponse)
            {
                return FhirResult.Create(fhirResponse.Response, fhirResponse.StatusCode);
            }

            if (response is IOperationActionResultResponse actionResponse)
            {
                return actionResponse.Response;
            }

            return new StatusCodeResult((int)response.StatusCode);
        }
    }
}
