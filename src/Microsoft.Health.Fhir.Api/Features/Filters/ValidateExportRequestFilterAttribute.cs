// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    /// <summary>
    /// A filter that validates the headers and parameters present in the export request.
    /// Short-circuits the pipeline if they are invalid.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal class ValidateExportRequestFilterAttribute : ActionFilterAttribute
    {
        private const string PreferHeaderName = "Prefer";
        private const string PreferHeaderExpectedValue = "respond-async";

        private readonly IExportDestinationClientFactory _exportDestinationClientFactory;

        // For now we will use a hard-coded list to determine what query parameters we will
        // allow for export requests. In the future, once we add export and other operations
        // to the capabilities statement, we can derive this list from there (via the ConformanceProvider).
        private readonly HashSet<string> _supportedQueryParams;

        public ValidateExportRequestFilterAttribute(IExportDestinationClientFactory exportDestinationClientFactory)
        {
            EnsureArg.IsNotNull(exportDestinationClientFactory, nameof(exportDestinationClientFactory));

            _exportDestinationClientFactory = exportDestinationClientFactory;

            _supportedQueryParams = new HashSet<string>(StringComparer.Ordinal)
            {
                KnownQueryParameterNames.DestinationType,
                KnownQueryParameterNames.DestinationConnectionSettings,
            };
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

            IQueryCollection queryCollection = context.HttpContext.Request.Query;

            // Validate that the request does not contain query parameters that are not supported.
            foreach (string paramName in queryCollection.Keys)
            {
                if (!_supportedQueryParams.Contains(paramName))
                {
                    throw new RequestNotValidException(string.Format(Resources.UnsupportedParameter, paramName));
                }
            }

            if (!queryCollection.TryGetValue(KnownQueryParameterNames.DestinationType, out StringValues destinationTypeValue)
               || string.IsNullOrWhiteSpace(destinationTypeValue)
               || !_exportDestinationClientFactory.IsSupportedDestinationType(destinationTypeValue))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedParameterValue, KnownQueryParameterNames.DestinationType));
            }

            if (!queryCollection.TryGetValue(KnownQueryParameterNames.DestinationConnectionSettings, out StringValues destinationSettingValue)
                || string.IsNullOrWhiteSpace(destinationSettingValue))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedParameterValue, KnownQueryParameterNames.DestinationConnectionSettings));
            }
        }
    }
}
