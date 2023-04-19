// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData;
using Microsoft.Health.Fhir.Core.Registration;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection
{
    public static class FhirServerBuilderConvertDataRegistrationExtensions
    {
        public static IFhirServerBuilder AddConvertData(this IFhirServerBuilder fhirServerBuilder)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));

            fhirServerBuilder.AddConvertDataTemplateProvider()
                .AddConvertDataEngine();

            return fhirServerBuilder;
        }

        private static IFhirServerBuilder AddConvertDataEngine(this IFhirServerBuilder fhirServerBuilder)
        {
            fhirServerBuilder.Services.AddSingleton<IConvertDataEngine, ConvertDataEngine>();

            return fhirServerBuilder;
        }

        private static IFhirServerBuilder AddConvertDataTemplateProvider(this IFhirServerBuilder fhirServerBuilder)
        {
            fhirServerBuilder.Services.AddSingleton<IConvertDataTemplateProvider, ContainerRegistryTemplateProvider>();
            fhirServerBuilder.Services.AddSingleton<IConvertDataTemplateProvider, DefaultTemplateProvider>();

            return fhirServerBuilder;
        }
    }
}
