// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Web;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public class StartupWithThrowingMiddleware : Startup
    {
        public StartupWithThrowingMiddleware(IConfiguration configuration)
            : base(configuration)
        {
        }

        public override void Configure(IApplicationBuilder app)
        {
            app.Use(async (context, next) =>
            {
                const string internalExceptionThrown = "internalExceptionThrown";

                var throwValue = context.Request.Query["throw"];

                switch (throwValue)
                {
                    // Internal is used to cause the ExceptionHandlerMiddleware logic to execute
                    case "internal":
                        // Only throw the error the first time that this path is executed.
                        // This allows the ExceptionHandlerMiddleware to continue to the error page on the second execution of this path.
                        if (!context.Items.ContainsKey(internalExceptionThrown))
                        {
                            context.Items[internalExceptionThrown] = true;
                            throw new Exception("internal exception");
                        }

                        break;

                    // Middleware is used to cause the BaseExceptionMiddleware logic to execute
                    case "middleware":
                        throw new Exception("middleware exception");
                }

                await next();
            });

            base.Configure(app);
        }
    }
}
