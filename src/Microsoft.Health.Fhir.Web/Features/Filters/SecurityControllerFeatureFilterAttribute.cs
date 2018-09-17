// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Web.Configs;

namespace Microsoft.Health.Fhir.Web.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    public class SecurityControllerFeatureFilterAttribute : ActionFilterAttribute
    {
        private readonly WebFeatureConfiguration _webFeatureConfiguration;

        public SecurityControllerFeatureFilterAttribute(IOptions<WebFeatureConfiguration> webFeatureConfiguration)
        {
            EnsureArg.IsNotNull(webFeatureConfiguration?.Value, nameof(webFeatureConfiguration));

            _webFeatureConfiguration = webFeatureConfiguration.Value;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (!_webFeatureConfiguration.SecurityControllersEnabled)
            {
                context.Result = new NotFoundResult();
            }

            base.OnActionExecuting(context);
        }
    }
}
