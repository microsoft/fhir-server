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
    public class NotImplementedExceptionFilterAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuted(ActionExecutedContext context)
        {
            if (context.Exception is NotImplementedException && !context.ExceptionHandled)
            {
                var resultJson = new JObject { ["error"] = context.Exception.Message };
                context.Result = new JsonResult(resultJson) { StatusCode = (int)HttpStatusCode.NotImplemented };
                context.ExceptionHandled = true;
            }
        }
    }
}
