// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Web.Modules
{
    public class ConformanceModule : IStartupModule
    {
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            // You can create your own ConformanceProvider by using your own implementation of IConfiguredConformanceProvider
            // here you can replace it with that implementation instead of using the DefaultConformanceProvider
            services.Replace(ServiceDescriptor.Singleton<IConfiguredConformanceProvider, DefaultConformanceProvider>());
        }
    }
}
