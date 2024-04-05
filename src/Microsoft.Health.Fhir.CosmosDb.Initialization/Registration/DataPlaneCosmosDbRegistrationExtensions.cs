// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;

using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures;
using Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage.StoredProcedures.AcquireExportJobs;
using Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage.StoredProcedures.HardDelete;
using Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage.StoredProcedures.Replace;
using Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage.StoredProcedures.UpdateUnsupportedSearchParametersToUnsupported;

namespace Microsoft.Health.Fhir.CosmosDb.Initialization.Registration
{
    public static class DataPlaneCosmosDbRegistrationExtensions
    {
        public static void AddDataPlaneDepencies(this IServiceCollection services)
        {
            // services.TypesInSameAssemblyAs<IStoredProcedureMetadata>()
            //   .AssignableTo<IStoredProcedureMetadata>()
            //   .Singleton()
            //   .AsSelf()
            //   .AsImplementedInterfaces()
            //   .AsService<IStoredProcedureMetadata>();

            services.Add<AcquireExportJobsMetadata>()
            .Transient()
            .AsImplementedInterfaces();
            services.Add<HardDeleteMetadata>()
            .Transient()
            .AsImplementedInterfaces();
            services.Add<ReplaceSingleResourceMetadata>()
            .Transient()
            .AsImplementedInterfaces();
            services.Add<UpdateUnsupportedSearchParametersMetadata>()
            .Transient()
            .AsImplementedInterfaces();
        }
    }
}
