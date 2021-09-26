// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Import.Shared.Core;

namespace Microsoft.Health.Fhir.Import.Core
{
    public static class ImportCoreRegistrationExtensions
    {
        public static IFhirServerBuilder AddImportOperationCore(this IFhirServerBuilder fhirServerBuilder, ImportTaskConfiguration importTaskConfiguration)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));
            EnsureArg.IsNotNull(importTaskConfiguration, nameof(importTaskConfiguration));

            fhirServerBuilder.Services.Add<ImportResourceLoader>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            fhirServerBuilder.Services.Add<ImportResourceParser>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            fhirServerBuilder.Services.Add<ImportErrorStoreFactory>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            fhirServerBuilder.Services.Add<ImportErrorSerializer>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            fhirServerBuilder.Services.Add<ImportTaskFactory>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            fhirServerBuilder.Services.AddSingleton(Options.Create(importTaskConfiguration));
            fhirServerBuilder.Services.AddSingleton(Options.Create(importTaskConfiguration));

            return fhirServerBuilder;
        }
    }
}
