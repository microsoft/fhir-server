// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Linq;
using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures;

namespace Microsoft.Health.Fhir.CosmosDb.Initialization.Registration
{
    public static class DataPlaneCosmosDbRegistrationExtensions
    {
        public static void AddCosmosDbInitializationDependencies(this IServiceCollection services)
        {
            Type[] storedProcedureMetadataTypes = typeof(DataPlaneCosmosDbRegistrationExtensions).Assembly.GetTypes()
                .Where(x => !x.IsAbstract && typeof(StoredProcedureMetadataBase).IsAssignableFrom(x))
                .Where(x => x.IsClass && !x.IsAbstract && !x.ContainsGenericParameters)
                .ToArray();

            foreach (Type type in storedProcedureMetadataTypes)
            {
                services
                    .Add(type)
                    .Singleton()
                    .AsSelf()
                    .AsService<IStoredProcedureMetadata>();
            }

            // Register the ArmClient factory delegate for ResourceManager collection setup
            services.AddSingleton<Func<TokenCredential, ArmClient>>(sp => tokenCredential => new ArmClient(tokenCredential));
        }
    }
}
