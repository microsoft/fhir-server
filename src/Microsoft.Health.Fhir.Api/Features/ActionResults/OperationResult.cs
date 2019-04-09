// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features.Export;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    public class OperationResult : ActionResult
    {
        public OperationResult()
        {
        }

        public OperationResult(OperationOutcome error)
        {
            EnsureArg.IsNotNull(error, nameof(error));

            Error = error;
        }

        public OperationResult(ExportJobResult jobResult)
        {
            EnsureArg.IsNotNull(jobResult, nameof(jobResult));

            JobResult = jobResult;
        }

        public HttpStatusCode? StatusCode { get; set; }

        public OperationOutcome Error { get; }

        public ExportJobResult JobResult { get; }

        internal IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

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

            // We will either have a JobResult or an Error, not both.
            ActionResult result;
            if (Error == null && JobResult == null)
            {
                result = new EmptyResult();
            }
            else if (Error == null)
            {
                result = new ObjectResult(JobResult);
            }
            else
            {
                result = new ObjectResult(Error);
            }

            return result.ExecuteResultAsync(context);
        }
    }
}
