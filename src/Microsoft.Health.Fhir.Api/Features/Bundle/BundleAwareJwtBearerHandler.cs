// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Encodings.Web;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Health.Fhir.Api.Features.Bundle
{
    public class BundleAwareJwtBearerHandler : JwtBearerHandler
    {
        private readonly IBundleHttpContextAccessor _bundleHttpContextAccessor;

        public BundleAwareJwtBearerHandler(IOptionsMonitor<JwtBearerOptions> options, ILoggerFactory logger, UrlEncoder encoder, IDataProtectionProvider dataProtection, ISystemClock clock, IBundleHttpContextAccessor bundleHttpContextAccessor)
            : base(options, logger, encoder, dataProtection, clock)
        {
            EnsureArg.IsNotNull(bundleHttpContextAccessor, nameof(bundleHttpContextAccessor));

            _bundleHttpContextAccessor = bundleHttpContextAccessor;
        }

        /// <summary>
        /// Override to check if the forbidden request is part of a bundle (batch/transaction). If it is, then set the status code of the internal request.
        /// </summary>
        /// <param name="properties">The authentication properties</param>
        /// <returns>Returns internal HandleForbiddenAsync Task.</returns>
        protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            if (_bundleHttpContextAccessor.HttpContext != null)
            {
                _bundleHttpContextAccessor.HttpContext.Response.StatusCode = 403;
            }

            await base.HandleForbiddenAsync(properties);
        }
    }
}
