// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Hosting;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Registration;

namespace Microsoft.Health.Fhir.Api.Modules
{
    /// <summary>
    /// Registration of search components.
    /// </summary>
    public static class SearchParametersModule
    {
        public static IFhirServerBuilder AddSearchParametersModules(this IFhirServerBuilder fhirServerBuilder)
        {
            fhirServerBuilder.Services.Add<SearchParameterStatusManager>()
                .Singleton()
                .AsSelf()
                .AsService<IHostedService>();

            fhirServerBuilder.Services.Add<SearchParameterResourceDataStore>()
                .Singleton()
                .AsSelf()
                .AsService<IHostedService>();

            return fhirServerBuilder;
        }
    }
}
