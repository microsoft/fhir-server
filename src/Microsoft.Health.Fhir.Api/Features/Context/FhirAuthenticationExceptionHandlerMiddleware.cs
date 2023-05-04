// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.S2S;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Health.Fhir.Api.Features.Context
{
    /// <summary>
    /// Middlware that runs before authentication middleware and after FhirRequestContextBeforeAuthenticationMiddleware
    /// </summary>
    public class FhirAuthenticationExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next;

        public FhirAuthenticationExceptionHandlerMiddleware(
            RequestDelegate next)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (S2SAuthenticationException ex)
            {
                if (ex.InnerException is SecurityTokenInvalidAudienceException || ex.InnerException is SecurityTokenInvalidIssuerException)
                {
                    throw ex.InnerException;
                }
                else
                {
                    throw;
                }
            }
        }
    }
}
