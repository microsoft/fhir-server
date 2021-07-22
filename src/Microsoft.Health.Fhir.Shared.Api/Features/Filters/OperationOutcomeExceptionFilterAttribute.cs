﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Bundle;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class OperationOutcomeExceptionFilterAttribute : ActionFilterAttribute
    {
        private const string ValidateController = "Validate";

        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;

        public OperationOutcomeExceptionFilterAttribute(RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
        }

        public override void OnActionExecuted(ActionExecutedContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (context?.Exception == null)
            {
                return;
            }

            if (context.Exception is FhirException fhirException)
            {
                var operationOutcomeResult = new OperationOutcomeResult(
                    new OperationOutcome
                    {
                        Id = _fhirRequestContextAccessor.RequestContext.CorrelationId,
                        Issue = fhirException.Issues.Select(x => x.ToPoco()).ToList(),
                    },
                    HttpStatusCode.BadRequest);

                switch (fhirException)
                {
                    case UnauthorizedFhirActionException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.Forbidden;
                        break;

                    case ResourceGoneException resourceGoneException:
                        operationOutcomeResult.StatusCode = HttpStatusCode.Gone;
                        if (!string.IsNullOrEmpty(resourceGoneException.DeletedResource?.VersionId))
                        {
                            operationOutcomeResult.Headers.Add(HeaderNames.ETag, WeakETag.FromVersionId(resourceGoneException.DeletedResource.VersionId).ToString());
                        }

                        break;
                    case ResourceNotFoundException _:
                    case JobNotFoundException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.NotFound;
                        break;
                    case JobConflictException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.Conflict;
                        break;
                    case MethodNotAllowedException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.MethodNotAllowed;
                        break;
                    case OpenIdConfigurationException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.ServiceUnavailable;
                        break;
                    case ResourceNotValidException _:
                        if (context.ActionDescriptor is ControllerActionDescriptor controllerDescriptor)
                        {
                            if (controllerDescriptor.ControllerName.Equals(ValidateController, StringComparison.OrdinalIgnoreCase))
                            {
                                operationOutcomeResult.StatusCode = HttpStatusCode.OK;
                                break;
                            }
                        }

                        operationOutcomeResult.StatusCode = HttpStatusCode.BadRequest;
                        break;
                    case BadRequestException _:
                    case RequestNotValidException _:
                    case BundleEntryLimitExceededException _:
                    case ProvenanceHeaderException _:
                    case RequestTooCostlyException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.BadRequest;
                        break;
                    case ResourceConflictException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.Conflict;
                        break;
                    case PreconditionFailedException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.PreconditionFailed;
                        break;
                    case InvalidSearchOperationException _:
                    case SearchOperationNotSupportedException _:
                    case CustomerManagedKeyException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.Forbidden;
                        break;
                    case UnsupportedConfigurationException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.InternalServerError;
                        break;
                    case OperationFailedException ofe:
                        operationOutcomeResult.StatusCode = ofe.ResponseStatusCode;
                        break;
                    case OperationNotImplementedException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.MethodNotAllowed;
                        break;
                    case NotAcceptableException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.NotAcceptable;
                        break;
                    case RequestEntityTooLargeException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.RequestEntityTooLarge;
                        break;
                    case FhirTransactionFailedException fhirTransactionFailedException:
                        operationOutcomeResult.StatusCode = fhirTransactionFailedException.ResponseStatusCode;
                        break;
                    case AzureContainerRegistryTokenException azureContainerRegistryTokenException:
                        operationOutcomeResult.StatusCode = azureContainerRegistryTokenException.StatusCode;
                        break;
                    case ConvertDataFailedException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.BadRequest;
                        break;
                    case FetchTemplateCollectionFailedException _:
                    case ConvertDataUnhandledException _:
                    case EverythingOperationException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.InternalServerError;
                        break;
                    case ConvertDataTimeoutException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.GatewayTimeout;
                        break;
                    case ConfigureCustomSearchException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.FailedDependency;
                        break;
                    case MemberMatchMatchingException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.UnprocessableEntity;
                        break;
                }

                context.Result = operationOutcomeResult;
                context.ExceptionHandled = true;
            }
            else if (context.Exception is MicrosoftHealthException microsoftHealthException)
            {
                OperationOutcomeResult healthExceptionResult;

                switch (microsoftHealthException)
                {
                    case RequestRateExceededException ex:
                        healthExceptionResult = CreateOperationOutcomeResult(ex.Message, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Throttled, HttpStatusCode.TooManyRequests);
                        healthExceptionResult.Headers.AddRetryAfterHeaders(ex.RetryAfter);

                        break;
                    case UnsupportedMediaTypeException unsupportedMediaTypeException:
                        healthExceptionResult = CreateOperationOutcomeResult(unsupportedMediaTypeException.Message, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.NotSupported, HttpStatusCode.UnsupportedMediaType);
                        break;
                    case ServiceUnavailableException serviceUnavailableException:
                        healthExceptionResult = CreateOperationOutcomeResult(serviceUnavailableException.Message, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Processing, HttpStatusCode.ServiceUnavailable);
                        break;
                    case TransactionFailedException transactionFailedException:
                        healthExceptionResult = CreateOperationOutcomeResult(transactionFailedException.Message, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Processing, HttpStatusCode.InternalServerError);
                        break;
                    case AuditException _:
                        healthExceptionResult = CreateOperationOutcomeResult(microsoftHealthException.Message, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Invalid, HttpStatusCode.BadRequest);
                        break;
                    case AuditHeaderCountExceededException _:
                    case AuditHeaderTooLargeException _:
                        healthExceptionResult = CreateOperationOutcomeResult(microsoftHealthException.Message, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Invalid, HttpStatusCode.RequestHeaderFieldsTooLarge);
                        break;
                    default:
                        healthExceptionResult = new OperationOutcomeResult(
                            new OperationOutcome
                            {
                                Id = _fhirRequestContextAccessor.RequestContext.CorrelationId,
                            },
                            HttpStatusCode.InternalServerError);
                        break;
                }

                context.Result = healthExceptionResult;
                context.ExceptionHandled = true;
            }
            else if (context.Exception.InnerException != null)
            {
                Exception outerException = context.Exception;
                context.Exception = outerException.InnerException;

                try
                {
                    OnActionExecuted(context);
                }
                finally
                {
                    if (!context.ExceptionHandled)
                    {
                        context.Exception = outerException;
                    }
                }
            }
        }

        private OperationOutcomeResult CreateOperationOutcomeResult(string message, OperationOutcome.IssueSeverity issueSeverity, OperationOutcome.IssueType issueType, HttpStatusCode httpStatusCode)
        {
            return new OperationOutcomeResult(
                new OperationOutcome
                {
                    Id = _fhirRequestContextAccessor.RequestContext.CorrelationId,
                    Issue = new List<OperationOutcome.IssueComponent>
                    {
                        new OperationOutcome.IssueComponent
                        {
                            Severity = issueSeverity,
                            Code = issueType,
                            Diagnostics = message,
                        },
                    },
                },
                httpStatusCode);
        }
    }
}
