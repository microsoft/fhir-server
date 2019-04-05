// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    /// <summary>
    /// Handles the output of a FHIR MVC Action Method
    /// </summary>
    public class FhirResult : BaseActionResult<ResourceElement>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FhirResult" /> class.
        /// </summary>
        public FhirResult()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirResult" /> class.
        /// </summary>
        /// <param name="resource">The resource.</param>
        public FhirResult(ResourceElement resource)
            : base(resource)
        {
        }

        /// <summary>
        /// Creates a FHIR result with the specified parameters
        /// </summary>
        /// <param name="resource">The resource.</param>
        /// <param name="statusCode">The status code.</param>
        public static FhirResult Create(ResourceElement resource, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            return new FhirResult(resource)
            {
                StatusCode = statusCode,
            };
        }

        /// <summary>
        /// Creates a Gone response
        /// </summary>
        public static FhirResult Gone()
        {
            return new FhirResult
            {
                StatusCode = HttpStatusCode.Gone,
            };
        }

        /// <summary>
        /// Returns a NotFound response
        /// </summary>
        public static FhirResult NotFound()
        {
            return new FhirResult
            {
                StatusCode = HttpStatusCode.NotFound,
            };
        }

        /// <summary>
        /// Returns a NoContent response
        /// </summary>
        public static FhirResult NoContent()
        {
            return new FhirResult
            {
                StatusCode = HttpStatusCode.NoContent,
            };
        }

        /// <inheritdoc />
        public override Task ExecuteResultAsync(ActionContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            HttpResponse response = context.HttpContext.Response;

            if (StatusCode.HasValue)
            {
                response.StatusCode = (int)StatusCode.Value;
            }

            foreach (KeyValuePair<string, StringValues> header in Headers)
            {
                response.Headers.Add(header);
            }

            ActionResult result;

            if (Result == null)
            {
                result = new EmptyResult();
            }
            else
            {
                result = new ObjectResult(Result.ToPoco());
            }

            return result.ExecuteResultAsync(context);
        }
    }
}
