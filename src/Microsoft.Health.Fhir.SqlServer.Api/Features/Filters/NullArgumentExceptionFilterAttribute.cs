// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.SqlServer.Api.Features.Filters
{
    public class NullArgumentExceptionFilterAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuted(ActionExecutedContext context)
        {
            if (context.Exception is ArgumentNullException && !context.ExceptionHandled)
            {
                var resultJson = new JObject();
                context.Result = new JsonResult(resultJson) { StatusCode = (int)HttpStatusCode.NotFound };
                context.ExceptionHandled = true;
            }
        }
    }
}
