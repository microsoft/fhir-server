// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Api.Features.Exceptions;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    internal class AadSmartOnFhirProxyExceptionFilterAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuted(ActionExecutedContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (context?.Exception == null)
            {
                return;
            }

            if (context.Exception is AadSmartOnFhirProxyBadRequestException && !context.ExceptionHandled)
            {
                context.Result = new BadRequestObjectResult(context.Exception.Message);
                context.ExceptionHandled = true;
            }
        }
    }
}
