// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Core.Configs;
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
        private readonly ExportJobConfiguration _exportJobConfiguration;

        // For now we will use a hard-coded list to determine what query parameters we will
        // allow for export requests. In the future, once we add export and other operations
        // to the capabilities statement, we can derive this list from there (via the ConformanceProvider).
        private readonly HashSet<string> _supportedQueryParams;

        public ValidateExportRequestFilterAttribute(IExportDestinationClientFactory exportDestinationClientFactory, IOptions<OperationsConfiguration> operationsConfig)
        {
            EnsureArg.IsNotNull(exportDestinationClientFactory, nameof(exportDestinationClientFactory));
            EnsureArg.IsNotNull(operationsConfig?.Value?.Export, nameof(operationsConfig));

            _exportDestinationClientFactory = exportDestinationClientFactory;
            _exportJobConfiguration = operationsConfig.Value.Export;

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

            // Both the destinationType and destinationConnectionSettings should be present or absent. Can't have one
            // and not the other.
            bool destinationTypePresent = queryCollection.TryGetValue(KnownQueryParameterNames.DestinationType, out StringValues destinationTypeValueFromQueryParam);
            bool destinationConnectionSettingsPresent = queryCollection.TryGetValue(KnownQueryParameterNames.DestinationConnectionSettings, out StringValues destinationConnectionSettingsValueFromQueryParam);

            // If only one of them is present, then the request is invalid.
            if ((!destinationTypePresent && destinationConnectionSettingsPresent) || (destinationTypePresent && !destinationConnectionSettingsPresent))
            {
                string missingParamName = destinationTypePresent ? KnownQueryParameterNames.DestinationConnectionSettings : KnownQueryParameterNames.DestinationType;
                throw new RequestNotValidException(string.Format(Resources.UnsupportedParameterValue, missingParamName));
            }

            // Get destination type, either from the query param or from the config. If both are present, query param
            // will be first priority.
            string destinationType = destinationTypePresent ? destinationTypeValueFromQueryParam.ToString() : _exportJobConfiguration.DefaultStorageAccountType;

            if (string.IsNullOrWhiteSpace(destinationType) || !_exportDestinationClientFactory.IsSupportedDestinationType(destinationType))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedParameterValue, KnownQueryParameterNames.DestinationType));
            }

            // Get destination connection settings from query param if present and validate.
            if (destinationConnectionSettingsPresent)
            {
                if (string.IsNullOrWhiteSpace(destinationConnectionSettingsValueFromQueryParam))
                {
                    throw new RequestNotValidException(string.Format(Resources.UnsupportedParameterValue, KnownQueryParameterNames.DestinationConnectionSettings));
                }

                // Validate whether the connection string is base-64 encoded.
                try
                {
                    Encoding.UTF8.GetString(Convert.FromBase64String(destinationConnectionSettingsValueFromQueryParam));
                }
                catch (Exception)
                {
                    throw new RequestNotValidException(string.Format(Resources.UnsupportedParameterValue, KnownQueryParameterNames.DestinationConnectionSettings));
                }
            }
            else
            {
                string destinationConnectionSettingsValueFromConfig = _exportJobConfiguration.DefaultStorageAccountConnection;
                if (string.IsNullOrWhiteSpace(destinationConnectionSettingsValueFromConfig))
                {
                    throw new RequestNotValidException(string.Format(Resources.UnsupportedParameterValue, _exportJobConfiguration.DefaultStorageAccountConnection));
                }

                // TODO: Should we validate whether it is a resource uri or a base-64 encoded connection settings?
            }
        }
    }
}
