// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Configs;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public class AadProxyFeatureFilterAttribute : ActionFilterAttribute
    {
        private readonly SecurityConfiguration _securityConfiguration;

        public AadProxyFeatureFilterAttribute(IOptions<SecurityConfiguration> securityConfiguration)
        {
            _securityConfiguration = securityConfiguration.Value;
        }

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            if (!_securityConfiguration.EnableAadProxy)
            {
                filterContext.Result = new UnauthorizedResult();
            }
            else
            {
                base.OnActionExecuting(filterContext);
            }
        }
    }
}