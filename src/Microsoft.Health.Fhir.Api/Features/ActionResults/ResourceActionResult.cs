﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    public abstract class ResourceActionResult<TResult> : ActionResult, IResourceActionResult
    {
        protected ResourceActionResult()
        {
            Headers = new HeaderDictionary();
        }

        protected ResourceActionResult(TResult result)
            : this()
        {
            Result = result;
        }

        protected ResourceActionResult(TResult result, HttpStatusCode statusCode)
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
        internal IHeaderDictionary Headers { get; }

        public override async Task ExecuteResultAsync(ActionContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            RequestContextAccessor<IFhirRequestContext> fhirContext = null;

            try
            {
                fhirContext = context.HttpContext.RequestServices.GetService<RequestContextAccessor<IFhirRequestContext>>();
            }
            catch (ObjectDisposedException ode)
            {
                throw new ServiceUnavailableException(Resources.NotAbleToCreateTheFinalResultsOfAnOperation, ode);
            }

            HttpResponse response = context.HttpContext.Response;

            if (fhirContext.GetMissingResourceCode() != null)
            {
                response.StatusCode = (int)fhirContext.GetMissingResourceCode().Value;
            }
            else if (StatusCode.HasValue)
            {
                response.StatusCode = (int)StatusCode.Value;
            }

            foreach (KeyValuePair<string, StringValues> header in Headers)
            {
                try
                {
                    response.Headers[header.Key] = header.Value;
                }
                catch (InvalidOperationException ioe)
                {
                    // Catching operations that change non-concurrent collections.
                    throw new InvalidOperationException($"Failed to set header '{header.Key}'.", ioe);
                }
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

            await result.ExecuteResultAsync(context);
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
