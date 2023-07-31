// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
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
            catch (Exception ex)
            {
                ThrowIfSecurityTokenException(ex);

                ExceptionDispatchInfo.Capture(ex).Throw();
            }
        }

        private static bool ThrowIfSecurityTokenException(Exception ex)
        {
            // Before checking if the current exception inherits from SecurityTokenException,
            // check if the inner exception is a SecurityTokenException.
            // That way, the most nested exception is raised.
            if (ex.InnerException != null)
            {
                ThrowIfSecurityTokenException(ex.InnerException);
            }

            if (ex is SecurityTokenException)
            {
                ExceptionDispatchInfo.Capture(ex).Throw();
            }

            return false;
        }
    }
}
