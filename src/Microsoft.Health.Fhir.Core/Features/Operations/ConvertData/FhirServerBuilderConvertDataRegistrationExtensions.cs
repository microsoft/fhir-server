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

            fhirServerBuilder.AddConvertDataTemplateProviders()
                .AddConvertDataEngine();

            return fhirServerBuilder;
        }

        private static IFhirServerBuilder AddConvertDataEngine(this IFhirServerBuilder fhirServerBuilder)
        {
            fhirServerBuilder.Services.AddSingleton<IConvertDataEngine, ConvertDataEngine>();

            return fhirServerBuilder;
        }

        private static IFhirServerBuilder AddConvertDataTemplateProviders(this IFhirServerBuilder fhirServerBuilder)
        {
            fhirServerBuilder.Services.AddSingleton<DefaultTemplateProvider>();
            fhirServerBuilder.Services.AddSingleton<ContainerRegistryTemplateProvider>();
            fhirServerBuilder.Services.AddSingleton<ITemplateProviderFactory, TemplateProviderFactory>();

            return fhirServerBuilder;
        }
    }
}
