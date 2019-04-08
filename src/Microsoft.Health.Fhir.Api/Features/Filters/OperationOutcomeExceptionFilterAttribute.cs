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
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Validation;

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
                var fhirResult = FhirResult.Create(
                    new OperationOutcome
                    {
                        Id = _fhirRequestContextAccessor.FhirRequestContext.CorrelationId,
                        Issue = fhirException.Issues.ToList(),
                    }, HttpStatusCode.BadRequest);

                switch (fhirException)
                {
                    case ResourceGoneException resourceGoneException:
                        fhirResult.StatusCode = HttpStatusCode.Gone;
                        if (!string.IsNullOrEmpty(resourceGoneException.DeletedResource?.VersionId))
                        {
                            fhirResult.SetETagHeader(WeakETag.FromVersionId(resourceGoneException.DeletedResource.VersionId));
                        }

                        break;
                    case ResourceNotFoundException _:
                    case JobNotFoundException _:
                        fhirResult.StatusCode = HttpStatusCode.NotFound;
                        break;
                    case MethodNotAllowedException _:
                        fhirResult.StatusCode = HttpStatusCode.MethodNotAllowed;
                        break;
                    case ServiceUnavailableException _:
                    case OpenIdConfigurationException _:
                        fhirResult.StatusCode = HttpStatusCode.ServiceUnavailable;
                        break;
                    case ResourceNotValidException _:
                    case BadRequestException _:
                    case RequestNotValidException _:
                        fhirResult.StatusCode = HttpStatusCode.BadRequest;
                        break;
                    case ResourceConflictException _:
                        fhirResult.StatusCode = HttpStatusCode.Conflict;
                        break;
                    case UnsupportedMediaTypeException _:
                        fhirResult.StatusCode = HttpStatusCode.UnsupportedMediaType;
                        break;
                    case PreconditionFailedException _:
                        fhirResult.StatusCode = HttpStatusCode.PreconditionFailed;
                        break;
                    case InvalidSearchOperationException _:
                    case SearchOperationNotSupportedException _:
                        fhirResult.StatusCode = HttpStatusCode.Forbidden;
                        break;
                    case UnsupportedConfigurationException _:
                    case AuditException _:
                        fhirResult.StatusCode = HttpStatusCode.InternalServerError;
                        break;
                    case OperationNotImplementedException _:
                        fhirResult.StatusCode = HttpStatusCode.NotImplemented;
                        break;
                }

                context.Result = fhirResult;
                context.ExceptionHandled = true;
            }
            else if (context.Exception is MicrosoftHealthException microsoftHealthException)
            {
                FhirResult healthExceptionResult;

                switch (microsoftHealthException)
                {
                    case RequestRateExceededException ex:
                        healthExceptionResult = FhirResult.Create(
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
                        healthExceptionResult = FhirResult.Create(
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
