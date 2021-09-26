// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Import.DataStore.SqlServer.DataGenerator;

namespace Microsoft.Health.Fhir.Import.DataStore.SqlServer
{
    public static class ImportSqlServerRegistrationExtensions
    {
        public static IFhirServerBuilder AddImportSqlServerDataStore(this IFhirServerBuilder fhirServerBuilder)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));
            IServiceCollection services = fhirServerBuilder.Services;

            services.Add<SqlImportOperation>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<SqlResourceBulkImporter>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<SqlResourceMetaPopulator>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<SqlBulkCopyDataWrapperFactory>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<DateTimeSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<NumberSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<QuantitySearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<ReferenceSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<StringSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<TokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<TokenSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<TokenStringCompositeSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<TokenTextSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<TokenTokenCompositeSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<UriSearchParamsTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<ResourceWriteClaimTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<CompartmentAssignmentTableBulkCopyDataGenerator>()
                .Transient()
                .AsSelf();

            services.Add<SqlStoreSequenceIdGenerator>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            return fhirServerBuilder;
        }
    }
}
