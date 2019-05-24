// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Controllers;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Routing
{
    public abstract class RouteTestBase
    {
        private DefaultHttpContext _defaultHttpContext;
        private readonly IRouter _router;
        private readonly ServiceProvider _buildServiceProvider;

        protected RouteTestBase()
        {
            var services = new ServiceCollection();
            services.AddRouting();
            services.AddOptions();
            services.AddSingleton<ILoggerFactory>(new NullLoggerFactory());

            AddAdditionalServices(services);

            _buildServiceProvider = services.BuildServiceProvider();

            var routeBuilder = new RouteBuilder(new ApplicationBuilder(_buildServiceProvider));

            MapControllerRoutes<FhirController>(routeBuilder);

            AddAdditionalRoutes(routeBuilder);

            _router = routeBuilder.Build();
        }

        protected virtual void AddAdditionalServices(IServiceCollection builder)
        {
        }

        protected virtual void AddAdditionalRoutes(IRouteBuilder builder)
        {
        }

        private void MapControllerRoutes<TController>(IRouteBuilder builder)
            where TController : Controller
        {
            foreach (var method in typeof(TController).GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                var routes = method.GetCustomAttributes<RouteAttribute>();
                var methods = method.GetCustomAttributes<HttpMethodAttribute>();

                if (routes != null)
                {
                    foreach (var route in routes)
                    {
                        if (methods != null)
                        {
                            foreach (var verb in methods.SelectMany(x => x.HttpMethods))
                            {
                                builder.MapVerb(verb, route.Template, context => Task.CompletedTask);
                            }
                        }
                        else
                        {
                            builder.MapRoute(route.Template, context => Task.CompletedTask);
                        }
                    }
                }
            }
        }

        protected async Task<RouteData> GetRouteData(string method, string path)
        {
            _defaultHttpContext = new DefaultHttpContext();
            _defaultHttpContext.RequestServices = _buildServiceProvider;
            _defaultHttpContext.Request.Path = path;
            _defaultHttpContext.Request.Method = method.ToUpper();

            var routeContext = new RouteContext(_defaultHttpContext);
            await _router.RouteAsync(routeContext);

            return routeContext.RouteData;
        }
    }
}
