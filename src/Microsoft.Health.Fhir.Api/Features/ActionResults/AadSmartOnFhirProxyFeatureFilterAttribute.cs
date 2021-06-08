// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class AadSmartOnFhirProxyFeatureFilterAttribute : ActionFilterAttribute
    {
        private readonly SecurityConfiguration _securityConfiguration;

        public AadSmartOnFhirProxyFeatureFilterAttribute(IOptions<SecurityConfiguration> securityConfiguration)
        {
            _securityConfiguration = securityConfiguration.Value;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (!_securityConfiguration.EnableAadSmartOnFhirProxy)
            {
                context.Result = new UnauthorizedResult();
            }
            else
            {
                base.OnActionExecuting(context);
            }
        }
    }
}
