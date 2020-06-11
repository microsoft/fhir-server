// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    /// <summary>
    /// A filter that validates the headers of a POST reindex request
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal class ValidateReindexRequestFilterAttribute : ActionFilterAttribute
    {
        private const string PreferHeaderName = "Prefer";
        private const string PreferHeaderExpectedValue = "respond-sync";

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            // If a header is included, it is a Prefer header with a value of respond-sync
            if (context.HttpContext.Request.Headers.TryGetValue(PreferHeaderName, out var preferHeaderValue) &&
                !string.Equals(preferHeaderValue[0], PreferHeaderExpectedValue, StringComparison.OrdinalIgnoreCase))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedHeaderValue, PreferHeaderName));
            }
        }
    }
}
