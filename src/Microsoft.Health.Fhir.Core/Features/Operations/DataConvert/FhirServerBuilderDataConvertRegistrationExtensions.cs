// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert;
using Microsoft.Health.Fhir.Core.Registration;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection
{
    public static class FhirServerBuilderDataConvertRegistrationExtensions
    {
        public static IFhirServerBuilder AddDataConvert(this IFhirServerBuilder fhirServerBuilder)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));

            fhirServerBuilder.AddDataConvertTemplateProvider()
                .AddDataConvertEngine();

            return fhirServerBuilder;
        }

        private static IFhirServerBuilder AddDataConvertEngine(this IFhirServerBuilder fhirServerBuilder)
        {
            fhirServerBuilder.Services.AddSingleton<IDataConvertEngine, DataConvertEngine>();

            return fhirServerBuilder;
        }

        private static IFhirServerBuilder AddDataConvertTemplateProvider(this IFhirServerBuilder fhirServerBuilder)
        {
            // Add memory cache to store template object and ACR access tokens
            // ToDo: add size limit
            fhirServerBuilder.Services.AddMemoryCache();
            fhirServerBuilder.Services.AddSingleton<IDataConvertTemplateProvider, ContainerRegistryTemplateProvider>();

            return fhirServerBuilder;
        }
    }
}
