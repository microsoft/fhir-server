// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Registration;

namespace Microsoft.AspNetCore.Builder
{
    public static class FhirServerApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseFhirServer(this IApplicationBuilder app)
        {
            EnsureArg.IsNotNull(app, nameof(app));

            IHostingEnvironment env = app.ApplicationServices.GetRequiredService<IHostingEnvironment>();
            ILoggerFactory loggerFactory = app.ApplicationServices.GetRequiredService<ILoggerFactory>();
            IApplicationLifetime appLifetime = app.ApplicationServices.GetRequiredService<IApplicationLifetime>();

            // This middleware will add delegates to the OnStarting method of httpContext.Response for setting headers.
            app.UseBaseHeaders();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                // This middleware will capture issues within other middleware that prevent the ExceptionHandler from completing.
                // This should be the first middleware added because they execute in order.
                app.UseBaseException();

                // This middleware will capture any unhandled exceptions and attempt to return an operation outcome using the customError page
                app.UseExceptionHandler("/CustomError");

                // This middleware will capture any handled error with the status code between 400 and 599 that hasn't had a body or content-type set. (i.e. 404 on unknown routes)
                app.UseStatusCodePagesWithReExecute("/CustomError", "?statusCode={0}");
            }

            app.UseAuthentication();

            foreach (var module in app.ApplicationServices.GetService<IEnumerable<IStartupConfiguration>>())
            {
                module.Configure(app, env, appLifetime, loggerFactory);
            }

            app.UseFhirRequestContext();
            app.UseStaticFiles();
            app.UseMvc();

            return app;
        }
    }
}
