// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Reflection;
using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Import.Core;

namespace Microsoft.Health.Fhir.Api.Features.Operations.Import
{
    public static class ImportServiceCollectionExtensions
    {
        private const string ImportOperationConfigurationSectionName = "FhirServer:Operations:Import";

        public static IFhirServerBuilder AddImport(this IFhirServerBuilder fhirServerBuilder, Action<IFhirServerBuilder> addImportStoreAction, IConfiguration configuration)
        {
            // EnsureArg.IsNotNull(addImportStoreAction, nameof(addImportStoreAction));
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            ImportTaskConfiguration importTaskConfiguration = new ImportTaskConfiguration();
            configuration.GetSection(ImportOperationConfigurationSectionName).Bind(importTaskConfiguration);

            if (importTaskConfiguration.Enabled)
            {
                Assembly importCoreAssembly = typeof(ImportConstants).Assembly;
                MediationModule.RegisterAssemblies(fhirServerBuilder.Services, importCoreAssembly);

                addImportStoreAction?.Invoke(fhirServerBuilder);

                return fhirServerBuilder.AddImportCore(importTaskConfiguration);
            }

            // Return fhirServerBuilder without import enable.
            return fhirServerBuilder;
        }
    }
}
