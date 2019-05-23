// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    public abstract class BaseActionResult<TResult> : ActionResult, IBaseActionResult
    {
        protected BaseActionResult()
        {
        }

        protected BaseActionResult(TResult result)
        {
            Result = result;
        }

        protected BaseActionResult(TResult result, HttpStatusCode statusCode)
            : this(result)
        {
            StatusCode = statusCode;
        }

        /// <summary>
        /// Gets the payload associated with this action result.
        /// </summary>
        public TResult Result { get; }

        /// <summary>
        /// Gets or sets the status code.
        /// </summary>
        public HttpStatusCode? StatusCode { get; set; }

        /// <summary>
        /// Gets or sets the action result Headers.
        /// </summary>
        internal IHeaderDictionary Headers { get; } = new HeaderDictionary();

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
                result = new ObjectResult(GetResultToSerialize());
            }

            return result.ExecuteResultAsync(context);
        }

        protected virtual object GetResultToSerialize()
        {
            return Result;
        }

        public virtual string GetResultTypeName()
        {
            return Result?.GetType().Name;
        }
    }
}
