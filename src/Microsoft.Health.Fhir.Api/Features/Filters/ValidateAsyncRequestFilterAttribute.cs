// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    /// <summary>
    /// A filter that validates the Prefer header of an async request
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ValidateAsyncRequestFilterAttribute : ActionFilterAttribute
    {
        private const string PreferHeaderExpectedValue = "respond-async";

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            // Checks that a Prefer header is included, and that it has a value of respond-async
            if (!context.HttpContext.Request.Headers.TryGetValue(KnownHeaders.Prefer, out var preferHeaderValue) ||
                !string.Equals(preferHeaderValue[0], PreferHeaderExpectedValue, StringComparison.OrdinalIgnoreCase))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedHeaderValue, KnownHeaders.Prefer));
            }
        }
    }
}
