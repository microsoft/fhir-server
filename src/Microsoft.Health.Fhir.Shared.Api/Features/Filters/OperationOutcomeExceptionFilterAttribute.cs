// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    internal class OperationOutcomeExceptionFilterAttribute : ActionFilterAttribute
    {
        private const string RetryAfterHeaderName = "x-ms-retry-after-ms";

        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;

        public OperationOutcomeExceptionFilterAttribute(IFhirRequestContextAccessor fhirRequestContextAccessor)
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
                        Id = _fhirRequestContextAccessor.FhirRequestContext.CorrelationId,
                        Issue = fhirException.Issues.Select(x => x.ToPoco()).ToList(),
                    }, HttpStatusCode.BadRequest);

                switch (fhirException)
                {
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
                    case MethodNotAllowedException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.MethodNotAllowed;
                        break;
                    case ServiceUnavailableException _:
                    case OpenIdConfigurationException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.ServiceUnavailable;
                        break;
                    case ResourceNotValidException _:
                    case BadRequestException _:
                    case RequestNotValidException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.BadRequest;
                        break;
                    case ResourceConflictException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.Conflict;
                        break;
                    case UnsupportedMediaTypeException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.UnsupportedMediaType;
                        break;
                    case PreconditionFailedException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.PreconditionFailed;
                        break;
                    case InvalidSearchOperationException _:
                    case SearchOperationNotSupportedException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.Forbidden;
                        break;
                    case UnsupportedConfigurationException _:
                    case AuditException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.InternalServerError;
                        break;
                    case AuditHeaderException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.RequestHeaderFieldsTooLarge;
                        break;
                    case OperationFailedException ofe:
                        operationOutcomeResult.StatusCode = ofe.ResponseStatusCode;
                        break;
                    case OperationNotImplementedException _:
                        operationOutcomeResult.StatusCode = HttpStatusCode.NotImplemented;
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
                        healthExceptionResult = new OperationOutcomeResult(
                            new OperationOutcome
                            {
                                Id = _fhirRequestContextAccessor.FhirRequestContext.CorrelationId,
                                Issue = new List<OperationOutcome.IssueComponent>
                                {
                                    new OperationOutcome.IssueComponent
                                    {
                                        Severity = OperationOutcome.IssueSeverity.Error,
                                        Code = OperationOutcome.IssueType.Throttled,
                                        Diagnostics = ex.Message,
                                    },
                                },
                            }, HttpStatusCode.BadRequest);
                        healthExceptionResult.StatusCode = HttpStatusCode.TooManyRequests;

                        if (ex.RetryAfter != null)
                        {
                            healthExceptionResult.Headers.Add(
                                RetryAfterHeaderName,
                                ex.RetryAfter.Value.TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
                        }

                        break;
                    default:
                        healthExceptionResult = new OperationOutcomeResult(
                            new OperationOutcome
                            {
                                Id = _fhirRequestContextAccessor.FhirRequestContext.CorrelationId,
                            }, HttpStatusCode.InternalServerError);
                        healthExceptionResult.StatusCode = HttpStatusCode.InternalServerError;
                        break;
                }

                context.Result = healthExceptionResult;
                context.ExceptionHandled = true;
            }
        }
    }
}
