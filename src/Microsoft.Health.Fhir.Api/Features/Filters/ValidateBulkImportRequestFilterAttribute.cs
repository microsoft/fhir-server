// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    /// <summary>
    /// A filter that validates the headers present in the export request.
    /// Short-circuits the pipeline if they are invalid.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal class ValidateBulkImportRequestFilterAttribute : ActionFilterAttribute
    {
        private const string PreferHeaderName = "Prefer";
        private const string PreferHeaderExpectedValue = "respond-async";
        private const string ContentTypeHeaderExpectedValue = "application/json";

        public ValidateBulkImportRequestFilterAttribute()
        {
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (!context.HttpContext.Request.Headers.TryGetValue(HeaderNames.Accept, out var acceptHeaderValue) ||
                acceptHeaderValue.Count != 1 ||
                !string.Equals(acceptHeaderValue[0], KnownContentTypes.JsonContentType, StringComparison.OrdinalIgnoreCase))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedHeaderValue, HeaderNames.Accept));
            }

            if (!context.HttpContext.Request.Headers.TryGetValue(PreferHeaderName, out var preferHeaderValue) ||
                preferHeaderValue.Count != 1 ||
                !string.Equals(preferHeaderValue[0], PreferHeaderExpectedValue, StringComparison.OrdinalIgnoreCase))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedHeaderValue, PreferHeaderName));
            }

            if (string.Equals(context.HttpContext.Request.Method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                if (!context.HttpContext.Request.Headers.TryGetValue(HeaderNames.ContentType, out var contentTypeHeaderValue) ||
                    preferHeaderValue.Count != 1 ||
                    !string.Equals(contentTypeHeaderValue[0], ContentTypeHeaderExpectedValue, StringComparison.OrdinalIgnoreCase))
                {
                    throw new RequestNotValidException(string.Format(Resources.UnsupportedHeaderValue, ContentTypeHeaderExpectedValue));
                }
            }
        }
    }
}
