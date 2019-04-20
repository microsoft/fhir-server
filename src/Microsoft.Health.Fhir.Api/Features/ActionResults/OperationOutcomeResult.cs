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
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    /// <summary>
    /// This action result is specifically used when we want to return an error
    /// to the client with the appropriate OperationOutcome.
    /// </summary>
    public class OperationOutcomeResult : ActionResult
    {
        public OperationOutcomeResult(OperationOutcome outcome, HttpStatusCode statusCode)
        {
            EnsureArg.IsNotNull(outcome, nameof(outcome));

            OperationOutcomeError = outcome;
            StatusCode = statusCode;
        }

        public OperationOutcome OperationOutcomeError { get; }

        public HttpStatusCode StatusCode { get; set; }

        public IHeaderDictionary Headers { get; } = new HeaderDictionary();

        public override Task ExecuteResultAsync(ActionContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            HttpResponse response = context.HttpContext.Response;
            response.StatusCode = (int)StatusCode;

            foreach (KeyValuePair<string, StringValues> header in Headers)
            {
                response.Headers.Add(header);
            }

            ActionResult result = new ObjectResult(OperationOutcomeError);

            return result.ExecuteResultAsync(context);
        }
    }
}
