// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Registration;

namespace Microsoft.Health.Fhir.Api.Features.RuntimeState
{
    /// <summary>
    /// Restricts deprecated FHIR services to requests needed to export data.
    /// </summary>
    public sealed class RuntimeStateMiddleware
    {
        internal const string DeprecatedServiceIssueCode = "service-deprecated";

        private const int MinimumRejectionDelayMilliseconds = 50;
        private const int MaximumRejectionDelayMilliseconds = 100;
        private const string ExportOperation = "$export";
        private const string PatientResourceType = "Patient";
        private const string GroupResourceType = "Group";
        private const string OperationsRouteSegment = "_operations";
        private const string ExportRouteSegment = "export";
        private const string DeprecatedServiceResponse =
            "{\"resourceType\":\"OperationOutcome\",\"issue\":[{\"severity\":\"error\",\"code\":\"business-rule\"," +
            "\"details\":{\"coding\":[{\"system\":\"https://azurehealthcareapis.com/fhir/operation-outcome-code\"," +
            "\"code\":\"service-deprecated\",\"display\":\"FHIR service deprecated\"}]," +
            "\"text\":\"This FHIR service has been deprecated.\"}," +
            "\"diagnostics\":\"This FHIR service no longer accepts normal workloads. Start a FHIR $export request " +
            "or retrieve an existing export status, then follow Azure Health Data Services migration guidance to " +
            "move the exported data to a supported FHIR service.\"}]}";

        private static readonly byte[] _deprecatedServiceResponse = Encoding.UTF8.GetBytes(DeprecatedServiceResponse);

        private readonly RequestDelegate _next;
        private readonly IFhirRuntimeConfiguration _runtimeConfiguration;

        /// <summary>
        /// Initializes a new instance of the <see cref="RuntimeStateMiddleware"/> class.
        /// </summary>
        /// <param name="next">The next request delegate.</param>
        /// <param name="runtimeConfiguration">The effective FHIR runtime configuration.</param>
        public RuntimeStateMiddleware(
            RequestDelegate next,
            IFhirRuntimeConfiguration runtimeConfiguration)
        {
            _next = EnsureArg.IsNotNull(next, nameof(next));
            _runtimeConfiguration = EnsureArg.IsNotNull(runtimeConfiguration, nameof(runtimeConfiguration));
        }

        /// <summary>
        /// Enforces the request allowlist when the service is deprecated.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        /// <returns>A task representing the request.</returns>
        public async Task Invoke(HttpContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (_runtimeConfiguration.RuntimeState != FhirRuntimeState.Deprecated || IsAllowedRequest(context.Request))
            {
                await _next(context);
                return;
            }

            int delayMilliseconds = RandomNumberGenerator.GetInt32(
                MinimumRejectionDelayMilliseconds,
                MaximumRejectionDelayMilliseconds + 1);
            await Task.Delay(delayMilliseconds, context.RequestAborted);

            context.Response.StatusCode = StatusCodes.Status410Gone;
            context.Response.ContentType = KnownContentTypes.JsonContentType;
            context.Response.ContentLength = _deprecatedServiceResponse.Length;
            await context.Response.Body.WriteAsync(_deprecatedServiceResponse.AsMemory(), context.RequestAborted);
        }

        private static bool IsAllowedRequest(HttpRequest request)
        {
            if (!HttpMethods.IsGet(request.Method))
            {
                return false;
            }

            string[] segments = GetPathSegments(request.Path);

            return IsHealthCheckRequest(segments) ||
                IsMetadataRequest(segments) ||
                IsExportStartRequest(segments) ||
                IsExportStatusRequest(segments);
        }

        private static string[] GetPathSegments(PathString path)
        {
            string pathValue = path.Value;
            if (string.IsNullOrEmpty(pathValue) || pathValue[0] != '/')
            {
                return null;
            }

            pathValue = pathValue.Substring(1);
            if (pathValue.EndsWith('/'))
            {
                pathValue = pathValue.Substring(0, pathValue.Length - 1);
            }

            return string.IsNullOrEmpty(pathValue) ? null : pathValue.Split('/');
        }

        private static bool IsHealthCheckRequest(string[] segments)
        {
            string[] healthCheckSegments = KnownRoutes.HealthCheck.Trim('/').Split('/');

            return segments?.Length == healthCheckSegments.Length &&
                string.Equals(segments[0], healthCheckSegments[0], StringComparison.OrdinalIgnoreCase) &&
                string.Equals(segments[1], healthCheckSegments[1], StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMetadataRequest(string[] segments)
        {
            return segments?.Length == 1 &&
                string.Equals(segments[0], KnownRoutes.Metadata, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExportStartRequest(string[] segments)
        {
            if (segments == null)
            {
                return false;
            }

            if (segments.Length == 1)
            {
                return string.Equals(segments[0], ExportOperation, StringComparison.OrdinalIgnoreCase);
            }

            if (segments.Length == 2)
            {
                return string.Equals(segments[0], PatientResourceType, StringComparison.Ordinal) &&
                    string.Equals(segments[1], ExportOperation, StringComparison.OrdinalIgnoreCase);
            }

            return segments.Length == 3 &&
                string.Equals(segments[0], GroupResourceType, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(segments[1]) &&
                string.Equals(segments[2], ExportOperation, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExportStatusRequest(string[] segments)
        {
            return segments?.Length == 3 &&
                string.Equals(segments[0], OperationsRouteSegment, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(segments[1], ExportRouteSegment, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(segments[2]);
        }
    }
}
