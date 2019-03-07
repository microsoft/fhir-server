// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Method)]
    internal class ValidateOperationHeadersFilterAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (context.HttpContext.Request.Headers.TryGetValue(HeaderNames.Accept, out var acceptHeaderValue))
            {
                if (acceptHeaderValue.Count != 1 || !string.Equals(acceptHeaderValue[0], "application/fhir+json", StringComparison.Ordinal))
                {
                    throw new ResourceNotValidException(new List<ValidationFailure>
                    {
                        new ValidationFailure(nameof(Base.TypeName), Resources.UnsupportedAcceptHeader),
                    });
                }
            }

            if (context.HttpContext.Request.Headers.TryGetValue("Prefer", out var preferHeaderValue))
            {
                if (preferHeaderValue.Count != 1 || !string.Equals(preferHeaderValue[0], "respond-async", StringComparison.Ordinal))
                {
                    throw new ResourceNotValidException(new List<ValidationFailure>
                    {
                        new ValidationFailure(nameof(Base.TypeName), Resources.UnsupportedPreferHeader),
                    });
                }
            }
        }
    }
}
