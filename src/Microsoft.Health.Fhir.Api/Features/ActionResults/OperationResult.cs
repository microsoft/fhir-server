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
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    /// <summary>
    /// Used to return the result of an operation. Currently supports only Export.
    /// </summary>
    public class OperationResult : ActionResult
    {
        public OperationResult(HttpStatusCode statusCode)
        {
            StatusCode = statusCode;
        }

        public OperationResult(ExportJobResult jobResult, HttpStatusCode statusCode)
        {
            EnsureArg.IsNotNull(jobResult, nameof(jobResult));

            JobResult = jobResult;
            StatusCode = statusCode;
        }

        public HttpStatusCode StatusCode { get; set; }

        public ExportJobResult JobResult { get; }

        internal IHeaderDictionary Headers { get; } = new HeaderDictionary();

        /// <summary>
        /// Creates a result with HttpStatusCode Accepted.
        /// </summary>
        public static OperationResult Accepted()
        {
            return new OperationResult(HttpStatusCode.Accepted);
        }

        /// <summary>
        /// Creates a result with HttpStatusCode Ok.
        /// </summary>
        /// <param name="jobResult">The job result that must be returned as part of the OperationResult.</param>
        public static OperationResult Ok(ExportJobResult jobResult)
        {
            EnsureArg.IsNotNull(jobResult);

            return new OperationResult(jobResult, HttpStatusCode.OK);
        }

        public override Task ExecuteResultAsync(ActionContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            HttpResponse response = context.HttpContext.Response;
            response.StatusCode = (int)StatusCode;

            foreach (KeyValuePair<string, StringValues> header in Headers)
            {
                response.Headers.Add(header);
            }

            ActionResult result;
            if (JobResult == null)
            {
                result = new EmptyResult();
            }
            else
            {
                result = new ObjectResult(JobResult);
            }

            return result.ExecuteResultAsync(context);
        }
    }
}
