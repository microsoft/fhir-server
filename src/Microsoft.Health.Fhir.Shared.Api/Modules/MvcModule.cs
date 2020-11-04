// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Binders;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Api.Models;
using Microsoft.Health.Fhir.Core.Features.Routing;

namespace Microsoft.Health.Fhir.Api.Modules
{
    public class MvcModule : IStartupModule
    {
        private readonly EmbeddedFileProvider _embeddedFileProvider;

        public MvcModule()
        {
            _embeddedFileProvider = new EmbeddedFileProvider(typeof(CodePreviewModel).Assembly);
        }

        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            // Adds route constraint for FHIR resource types
            services.Configure<RouteOptions>(options =>
            {
                options.ConstraintMap.Add(KnownRoutes.ResourceTypeRouteConstraint, typeof(ResourceTypesRouteConstraint));
                options.ConstraintMap.Add(KnownRoutes.ResourceIdRouteConstraint, typeof(ResourceIdRouteConstraint));
                options.ConstraintMap.Add(KnownRoutes.CompartmentTypeRouteConstraint, typeof(CompartmentTypesRouteConstraint));
                options.ConstraintMap.Add(KnownRoutes.CompartmentResourceTypeRouteConstraint, typeof(CompartmentResourceTypesRouteConstraint));
            });

            // Adds provider to serve embedded razor views
            services.Configure<MvcRazorRuntimeCompilationOptions>(options =>
            {
                options.FileProviders.Add(_embeddedFileProvider);
            });

            services.PostConfigure<MvcOptions>(options =>
            {
                options.ModelBinderProviders.Insert(0, new PartialDateTimeBinderProvider());

                // This filter should run first because it populates data for FhirRequestContext.
                options.Filters.Add(typeof(FhirRequestContextRouteDataPopulatingFilterAttribute), 0);
            });

            services.AddHttpContextAccessor();

            // These are needed for IUrlResolver used by search.
            // If we update the search implementation to not use these, we should remove
            // the registration since enabling these accessors has performance implications.
            // https://github.com/aspnet/Hosting/issues/793
            services.TryAddSingleton<IActionContextAccessor, ActionContextAccessor>();

            services.Add<QueryStringParser>()
                .Singleton()
                .AsService<IQueryStringParser>();
        }
    }
}
