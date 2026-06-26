// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Context
{
    /// <summary>
    /// TEST-ONLY middleware that lets a caller override allow-listed server configuration values for a
    /// single request. It is a no-op unless <see cref="FeatureConfiguration.SupportsRequestConfigurationOverrides"/>
    /// is enabled (disabled by default; intended to be turned on only in the end-to-end test environment).
    /// <para>
    /// Overrides may be supplied either as query-string parameters prefixed with <see cref="QueryParameterPrefix"/>
    /// (for example <c>?_config.EnableFhirDateContainment=true</c>) or as request headers prefixed with
    /// <see cref="HeaderPrefix"/> (for example <c>X-FHIRServer-Config-EnableFhirDateContainment: true</c>).
    /// The bare configuration name (the part after the prefix) becomes the override key. When the same key is
    /// supplied via both transports, the header value wins. Recognized <see cref="QueryParameterPrefix"/>
    /// query parameters are stripped from the request so they never reach the FHIR search parameter parser.
    /// </para>
    /// </summary>
    public class RequestConfigurationOverrideMiddleware
    {
        /// <summary>
        /// Prefix that marks a query-string parameter as a server configuration override.
        /// </summary>
        public const string QueryParameterPrefix = "_config.";

        /// <summary>
        /// Prefix that marks a request header as a server configuration override.
        /// </summary>
        public const string HeaderPrefix = "X-FHIRServer-Config-";

        private readonly RequestDelegate _next;
        private readonly FeatureConfiguration _featureConfiguration;
        private readonly ILogger<RequestConfigurationOverrideMiddleware> _logger;

        public RequestConfigurationOverrideMiddleware(
            RequestDelegate next,
            IOptions<FhirServerConfiguration> fhirServerConfiguration,
            ILogger<RequestConfigurationOverrideMiddleware> logger)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(fhirServerConfiguration?.Value, nameof(fhirServerConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _next = next;
            _featureConfiguration = fhirServerConfiguration.Value.Features;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context, RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));

            // Security gate: do nothing at all unless the test-only feature is explicitly enabled.
            if (_featureConfiguration.SupportsRequestConfigurationOverrides)
            {
                var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                ApplyQueryStringOverrides(context, overrides);
                ApplyHeaderOverrides(context, overrides);

                if (overrides.Count > 0)
                {
                    fhirRequestContextAccessor.RequestContext?.SetRequestConfigurationOverrides(overrides);

                    _logger.LogInformation(
                        "Applied {Count} per-request configuration override(s): {Keys}",
                        overrides.Count,
                        string.Join(", ", overrides.Keys));
                }
            }

            await _next(context);
        }

        private static void ApplyQueryStringOverrides(HttpContext context, Dictionary<string, string> overrides)
        {
            if (!context.Request.QueryString.HasValue)
            {
                return;
            }

            var remainingQuery = new List<KeyValuePair<string, string>>();
            bool removedAny = false;

            foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> pair in context.Request.Query)
            {
                if (pair.Key.StartsWith(QueryParameterPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string overrideKey = pair.Key.Substring(QueryParameterPrefix.Length);
                    if (!string.IsNullOrEmpty(overrideKey))
                    {
                        // Last value wins for repeated keys.
                        overrides[overrideKey] = pair.Value.ToString();
                        removedAny = true;
                    }
                }
                else
                {
                    foreach (string value in pair.Value)
                    {
                        remainingQuery.Add(new KeyValuePair<string, string>(pair.Key, value));
                    }
                }
            }

            if (removedAny)
            {
                // Strip the override parameters so the FHIR search parameter parser (which reads the raw
                // query string) never sees them and never reports them as unsupported parameters.
                context.Request.QueryString = QueryString.Create(remainingQuery);
            }
        }

        private static void ApplyHeaderOverrides(HttpContext context, Dictionary<string, string> overrides)
        {
            foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> header in context.Request.Headers)
            {
                if (header.Key.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string overrideKey = header.Key.Substring(HeaderPrefix.Length);
                    if (!string.IsNullOrEmpty(overrideKey))
                    {
                        // Header wins over a query-string override of the same name.
                        overrides[overrideKey] = header.Value.ToString();
                    }
                }
            }
        }
    }
}
