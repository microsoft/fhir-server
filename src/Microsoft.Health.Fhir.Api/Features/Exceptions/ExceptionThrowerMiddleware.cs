// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Exceptions
{
    public class ExceptionThrowerMiddleware
    {
        private const string InternalExceptionThrown = "internalExceptionThrown";
        private readonly RequestDelegate _next;

        public ExceptionThrowerMiddleware(RequestDelegate next)
        {
            EnsureArg.IsNotNull(next, nameof(next));

            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var throwValue = context.Request.Query["throw"];

            switch (throwValue)
            {
                // Internal is used to cause the ExceptionHandlerMiddleware logic to execute
                case "internal":
                    // Only throw the error the first time that this path is executed.
                    // This allows the ExceptionHandlerMiddleware to continue to the error page on the second execution of this path.
                    if (!context.Items.ContainsKey(InternalExceptionThrown))
                    {
                        context.Items[InternalExceptionThrown] = true;
                        throw new Exception("internal exception");
                    }

                    break;

                // Middleware is used to cause the BaseExceptionMiddleware logic to execute
                case "middleware":
                    throw new Exception("middleware exception");
            }

            await _next(context);
        }
    }
}
