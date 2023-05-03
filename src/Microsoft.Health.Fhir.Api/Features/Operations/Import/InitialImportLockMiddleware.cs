// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Extensions;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Api.Features.Operations.Import
{
    public sealed class InitialImportLockMiddleware
    {
        private RequestDelegate _next;
        private ImportTaskConfiguration _importJobConfiguration;
        private readonly HashSet<(string method, string pathRegex)> _excludedEndpoints;
        private readonly HashSet<(string method, string pathRegex)> _filteredEndpoints;

        // hard-coding these to minimize resource consumption for locked message
        private const string LockedContentType = "application/json; charset=utf-8";
        private static readonly ReadOnlyMemory<byte> _lockedBody = CreateLockedBody(Resources.LockedForInitialImportMode);

        public InitialImportLockMiddleware(
            RequestDelegate next,
            IOptions<ImportTaskConfiguration> importJobConfiguration)
        {
            _next = EnsureArg.IsNotNull(next, nameof(next));
            _importJobConfiguration = EnsureArg.IsNotNull(importJobConfiguration?.Value, nameof(importJobConfiguration));

            _excludedEndpoints = new HashSet<(string method, string pathRegex)>()
            {
                (HttpMethods.Get, ".*"), // Exclude all read operations
                (HttpMethods.Post, ".*/\\$import"),
                (HttpMethods.Delete, ".*/_operations/.+/.+"), // Allow the cancelation of any long running job
            };

            _filteredEndpoints = new HashSet<(string method, string pathRegex)>()
            {
                (HttpMethods.Get, ".*/\\$reindex"), // New long running jobs shouldn't be started while import is running
            };
        }

        public async Task Invoke(HttpContext context)
        {
            if (!context.Request.IsFhirRequest() || !_importJobConfiguration.Enabled || !_importJobConfiguration.InitialImportMode)
            {
                await _next(context);
                return;
            }

            if (IsExcludedEndpoint(context.Request.Method, context.Request.Path))
            {
                await _next(context);
                return;
            }

            await Return423(context);
        }

        private static async Task Return423(HttpContext context)
        {
            context.Response.StatusCode = StatusCodes.Status423Locked;
            context.Response.ContentLength = _lockedBody.Length;
            context.Response.ContentType = LockedContentType;

            await context.Response.Body.WriteAsync(_lockedBody);
        }

        private bool IsExcludedEndpoint(string method, string path)
        {
            return _excludedEndpoints.Any(endpoint =>
                                            endpoint.method.Equals(method, StringComparison.OrdinalIgnoreCase) &&
                                            Regex.IsMatch(path, endpoint.pathRegex, RegexOptions.IgnoreCase)) &&
                   !_filteredEndpoints.Any(endpoint =>
                                            endpoint.method.Equals(method, StringComparison.OrdinalIgnoreCase) &&
                                            Regex.IsMatch(path, endpoint.pathRegex, RegexOptions.IgnoreCase));
        }

        private static Memory<byte> CreateLockedBody(string message) => Encoding.UTF8.GetBytes($@"{{""severity"":""Error"",""code"":""Locked"",""diagnostics"":""{message}""}}").AsMemory();
    }
}
