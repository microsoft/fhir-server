// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    /// <summary>
    /// A filter that validates the headers present in the export request.
    /// Short-circuits the pipeline if they are invalid.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ValidateExportRequestFilterAttribute : ActionFilterAttribute
    {
        private static readonly HashSet<string> SupportedOutputFormats = new HashSet<string>(StringComparer.Ordinal)
        {
            "application/fhir+ndjson",
            "application/ndjson",
            "ndjson",
        };

        private const string PreferHeaderName = "Prefer";
        private const string PreferHeaderValueRequired = "respond-async";
        private const string PreferHeaderValueOptional = "handling";

        private readonly HashSet<string> _supportedQueryParams;

        public ValidateExportRequestFilterAttribute()
        {
            _supportedQueryParams = new HashSet<string>(StringComparer.Ordinal)
            {
                KnownQueryParameterNames.OutputFormat,
                KnownQueryParameterNames.Since,
                KnownQueryParameterNames.Till,
                KnownQueryParameterNames.Type,
                KnownQueryParameterNames.Container,
                KnownQueryParameterNames.Format,
                KnownQueryParameterNames.TypeFilter,
                KnownQueryParameterNames.IsParallel,
                KnownQueryParameterNames.IncludeAssociatedData,
                KnownQueryParameterNames.MaxCount,
                KnownQueryParameterNames.AnonymizationConfigurationCollectionReference,
                KnownQueryParameterNames.AnonymizationConfigurationLocation,
                KnownQueryParameterNames.AnonymizationConfigurationFileEtag,
            };
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (!context.HttpContext.Request.Headers.TryGetValue(HeaderNames.Accept, out var acceptHeaderValue) ||
                acceptHeaderValue.Count != 1 ||
                !string.Equals(acceptHeaderValue[0], KnownContentTypes.JsonContentType, StringComparison.OrdinalIgnoreCase))
            {
                throw new RequestNotValidException(string.Format(Api.Resources.UnsupportedHeaderValue, acceptHeaderValue.FirstOrDefault(), HeaderNames.Accept));
            }

            if (context.HttpContext.Request.Headers.TryGetValue(PreferHeaderName, out var preferHeaderValues))
            {
                var values = preferHeaderValues.SelectMany(x => x.Split(',', StringSplitOptions.TrimEntries)).ToList();
                var requiredHeaderValueFound = false;
                foreach (var value in values)
                {
                    var v = value.Split('=', StringSplitOptions.TrimEntries);
                    if (v.Length > 2
                        || (v.Length == 1 && !(requiredHeaderValueFound = string.Equals(v[0], PreferHeaderValueRequired, StringComparison.OrdinalIgnoreCase)))
                        || (v.Length == 2 && (!string.Equals(v[0], PreferHeaderValueOptional, StringComparison.OrdinalIgnoreCase) || !Enum.TryParse<SearchParameterHandling>(v[1], true, out _))))
                    {
                        throw new RequestNotValidException(string.Format(Api.Resources.UnsupportedHeaderValue, value, PreferHeaderName));
                    }
                }

                if (!requiredHeaderValueFound)
                {
                    throw new RequestNotValidException($"Missing required header value; header={PreferHeaderName},value={PreferHeaderValueRequired}.");
                }
            }
            else
            {
                throw new RequestNotValidException($"Missing required header; {PreferHeaderName}");
            }

            // Validate that the request does not contain query parameters that are not supported.
            IQueryCollection queryCollection = context.HttpContext.Request.Query;
            foreach (string paramName in queryCollection?.Keys)
            {
                if (IsValidBasicExportRequestParam(paramName))
                {
                    continue;
                }

                throw new RequestNotValidException(string.Format(Api.Resources.UnsupportedParameter, paramName));
            }

            if (queryCollection?.Keys != null &&
                queryCollection.Keys.Contains(KnownQueryParameterNames.TypeFilter) &&
                !queryCollection.Keys.Contains(KnownQueryParameterNames.Type))
            {
                throw new RequestNotValidException(Api.Resources.TypeFilterWithoutTypeIsUnsupported);
            }

            if (queryCollection.TryGetValue(KnownQueryParameterNames.OutputFormat, out var outputFormats))
            {
                foreach (var outputFormat in outputFormats)
                {
                    if (!(outputFormat == null || SupportedOutputFormats.Contains(outputFormat)))
                    {
                        throw new RequestNotValidException(string.Format(Api.Resources.InvalidOutputFormat, outputFormat));
                    }
                }
            }
        }

        private bool IsValidBasicExportRequestParam(string paramName)
        {
            return _supportedQueryParams.Contains(paramName);
        }
    }
}
