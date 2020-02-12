// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Registration;

namespace Microsoft.Health.Fhir.Azure
{
    public static class FhirServerBuilderAzureRegistrationExtensions
    {
        public static IFhirServerBuilder AddAzureExportDestinationClient(this IFhirServerBuilder fhirServerBuilder)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));

            fhirServerBuilder.Services.Add<AzureExportDestinationClient>()
                .Transient()
                .AsSelf();

            fhirServerBuilder.Services.Add<Func<IExportDestinationClient>>(sp => () => sp.GetRequiredService<AzureExportDestinationClient>())
                .Transient()
                .AsSelf();

            return fhirServerBuilder;
        }

        public static IFhirServerBuilder AddAzureAccessTokenProvider(this IFhirServerBuilder fhirServerBuilder)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));

            fhirServerBuilder.Services.Add<AzureAccessTokenProvider>()
                .Transient()
                .AsSelf();

            return fhirServerBuilder;
        }
    }
}
