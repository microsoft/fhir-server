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
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Api.Features.Bundle
{
    /// <summary>
    /// This class extends the standard JwtBearerHandler to handle cases when batches and transactions are processed.
    /// Because the pipeline is only setup once, the context used in portions of the pipeline is set to the POST on / for batches/transactions and not the sub-calls.
    /// To counteract this behavior in the case of sub-calls we check to see if the BundleHttpContextAccessor has an HttpContext and set its response status code.
    /// </summary>
    public class BundleAwareJwtBearerHandler : JwtBearerHandler
    {
        private readonly IBundleHttpContextAccessor _bundleHttpContextAccessor;
        private readonly IAuditHelper _auditHelper;
        private readonly IClaimsExtractor _claimsExtractor;

        public BundleAwareJwtBearerHandler(
            IOptionsMonitor<JwtBearerOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IDataProtectionProvider dataProtection,
            ISystemClock clock,
            IBundleHttpContextAccessor bundleHttpContextAccessor,
            IAuditHelper auditHelper,
            IClaimsExtractor claimsExtractor)
            : base(options, logger, encoder, dataProtection, clock)
        {
            EnsureArg.IsNotNull(bundleHttpContextAccessor, nameof(bundleHttpContextAccessor));
            EnsureArg.IsNotNull(auditHelper, nameof(auditHelper));
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));

            _bundleHttpContextAccessor = bundleHttpContextAccessor;
            _auditHelper = auditHelper;
            _claimsExtractor = claimsExtractor;
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
                _auditHelper.LogExecuted(_bundleHttpContextAccessor.HttpContext, _claimsExtractor);
            }

            await base.HandleForbiddenAsync(properties);
        }
    }
}
