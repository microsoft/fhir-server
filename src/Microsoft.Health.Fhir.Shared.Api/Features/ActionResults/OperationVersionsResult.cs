// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Fhir.Core.Features.Operations.Versions;
using Microsoft.Net.Http.Headers;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    /// <summary>
    /// Used to return the result of a versions operation.
    /// </summary>
    public class OperationVersionsResult : ResourceActionResult<VersionsResult>
    {
        private readonly VersionsResult _versionsResult;

        private HttpContext _httpContext;
        private IList<MediaTypeHeaderValue> _acceptHeaders;

        /// <summary>
        /// Initializes a new instance of the <see cref="OperationVersionsResult"/> class.
        /// </summary>
        /// <param name="versionsResult">The result for the versions operation.</param>
        /// <param name="statusCode">The HTTP status code.</param>
        public OperationVersionsResult(VersionsResult versionsResult, HttpStatusCode statusCode)
            : base(versionsResult, statusCode)
        {
            EnsureArg.IsNotNull(versionsResult, nameof(versionsResult));
            _versionsResult = versionsResult;
        }

        public override Task ExecuteResultAsync(ActionContext context)
        {
            _httpContext = context.HttpContext;
            _acceptHeaders = _httpContext.Request.GetTypedHeaders().Accept;
            return base.ExecuteResultAsync(context);
        }

        protected override object GetResultToSerialize()
        {
            if (_acceptHeaders != null && _acceptHeaders.All(a => a.MediaType != "*/*"))
            {
                foreach (MediaTypeHeaderValue acceptHeader in _acceptHeaders)
                {
                    // If the accept header is application/[json/xml]
                    if (string.Equals(acceptHeader.SubType.ToString(), ContentType.FORMAT_PARAM_XML, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(acceptHeader.SubType.ToString(), ContentType.FORMAT_PARAM_JSON, StringComparison.OrdinalIgnoreCase))
                    {
                        // Follow the format outlined in the spec: https://www.hl7.org/fhir/capabilitystatement-operation-versions.html.
                        return _versionsResult;
                    }

                    // If the accept header is application/fhir+[json/xml]
                    if (string.Equals("fhir", acceptHeader.Copy().SubTypeWithoutSuffix.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        // The returned information should be formatted as a Parameters object.
                        return FormatVersionsResultAsParameters();
                    }
                }
            }

            // If no accept header is specified, return the versions result as a JSON-formatted Parameters object.
            return FormatVersionsResultAsParameters();
        }

        private Parameters FormatVersionsResultAsParameters()
        {
            var supportedVersion = new FhirString(_versionsResult.Versions.First());
            var defaultVersion = new FhirString(_versionsResult.DefaultVersion);

            Parameters parameters = new Parameters()
                .Add("version", supportedVersion)
                .Add("default", defaultVersion);
            return parameters;
        }
    }
}
