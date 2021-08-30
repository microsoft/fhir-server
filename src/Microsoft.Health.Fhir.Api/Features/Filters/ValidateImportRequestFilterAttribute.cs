// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    /// <summary>
    /// A filter that validates the headers present in the import request.
    /// Short-circuits the pipeline if they are invalid.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ValidateImportRequestFilterAttribute : ActionFilterAttribute
    {
        private const string PreferHeaderName = "Prefer";
        private const string PreferHeaderExpectedValue = "respond-async";
        private const string ContentTypeHeaderExpectedValue = "application/fhir+json";

        public ValidateImportRequestFilterAttribute()
        {
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (!context.HttpContext.Request.Headers.TryGetValue(PreferHeaderName, out var preferHeaderValue) ||
                preferHeaderValue.Count != 1 ||
                !string.Equals(preferHeaderValue[0], PreferHeaderExpectedValue, StringComparison.OrdinalIgnoreCase))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedHeaderValue, PreferHeaderName));
            }

            if (string.Equals(context.HttpContext.Request.Method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                if (!context.HttpContext.Request.Headers.TryGetValue(HeaderNames.ContentType, out var contentTypeHeaderValue) ||
                    contentTypeHeaderValue.Count != 1 ||
                    !contentTypeHeaderValue[0].Contains(ContentTypeHeaderExpectedValue, StringComparison.OrdinalIgnoreCase))
                {
                    throw new RequestNotValidException(string.Format(Resources.UnsupportedHeaderValue, HeaderNames.ContentType));
                }
            }
        }
    }
}
