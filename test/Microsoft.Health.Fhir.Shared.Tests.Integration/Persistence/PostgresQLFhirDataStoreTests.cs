// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.PostgresQL;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class PostgresQLFhirDataStoreTests
    {
        private const string LocalConnectionString = "server=(local);Integrated Security=true;TrustServerCertificate=True";

        private IFhirDataStore _fhirDataStore;
        private SchemaInformation _schemaInformation;
        private SearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly FilebasedSearchParameterStatusDataStore _filebasedSearchParameterStatusDataStore;
        private SqlTransactionHandler _sqlTransactionHandler;
        private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private DefaultSqlConnectionBuilder _sqlConnectionBuilder;
        private readonly RawResourceFactory _rawResourceFactory;
        private SqlServerFhirModel _sqlServerFhirModel;

        public PostgresQLFhirDataStoreTests()
        {
            _searchParameterDefinitionManager = new SearchParameterDefinitionManager(ModelInfoProvider.Instance, _mediator, () => _searchService.CreateMockScope(), NullLogger<SearchParameterDefinitionManager>.Instance);
            _searchParameterDefinitionManager.StartAsync(CancellationToken.None);

            _filebasedSearchParameterStatusDataStore = new FilebasedSearchParameterStatusDataStore(_searchParameterDefinitionManager, ModelInfoProvider.Instance);

            var securityConfiguration = new SecurityConfiguration { PrincipalClaims = { "oid" } };
            IOptions<CoreFeatureConfiguration> coreFeatures = null;
            IOptions<CoreFeatureConfiguration> options = coreFeatures ?? Options.Create(new CoreFeatureConfiguration());

            var schemaOptions = new SqlServerSchemaOptions { AutomaticUpdatesEnabled = true };
            var initialConnectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalConnectionString;
            var databaseName = "FHIR_R4";
            var sqlConnectionStringBuilder = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = databaseName }.ToString();
            var config = Options.Create(new SqlServerDataStoreConfiguration { ConnectionString = sqlConnectionStringBuilder, Initialize = true, SchemaOptions = schemaOptions, StatementTimeout = TimeSpan.FromMinutes(10) });
            var sqlConnectionStringProvider = new DefaultSqlConnectionStringProvider(config);
            SqlRetryLogicBaseProvider sqlRetryLogicBaseProvider = SqlConfigurableRetryFactory.CreateFixedRetryProvider(new SqlClientRetryOptions().Settings);
            _sqlConnectionBuilder = new DefaultSqlConnectionBuilder(sqlConnectionStringProvider, sqlRetryLogicBaseProvider);
            _sqlTransactionHandler = new SqlTransactionHandler();
            _sqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(_sqlTransactionHandler, _sqlConnectionBuilder, sqlRetryLogicBaseProvider, config);

            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, 39) { Current = 39};
            _sqlServerFhirModel = new SqlServerFhirModel(
                _schemaInformation,
                _searchParameterDefinitionManager,
                () => _filebasedSearchParameterStatusDataStore,
                Options.Create(securityConfiguration),
                () => _sqlConnectionWrapperFactory.CreateMockScope(),
                Substitute.For<IMediator>(),
                NullLogger<SqlServerFhirModel>.Instance);

            _fhirDataStore = new PostgresQLFhirDataStore(
                _sqlServerFhirModel,
                _schemaInformation,
                NullLogger<PostgresQLFhirDataStore>.Instance,
                new CompressedRawResourceConverter(),
                options,
                ModelInfoProvider.Instance);

            _rawResourceFactory = new RawResourceFactory(new Hl7.Fhir.Serialization.FhirJsonSerializer());
        }

        [Fact]
        public async Task GivenUpsertResource_WhenRun_ThenDataShouldBeUpsert()
        {
            await _sqlServerFhirModel.Initialize(39, true, CancellationToken.None);
            var patient = Samples.GetDefaultPatient().UpdateVersion("3").UpdateLastUpdated(Clock.UtcNow - TimeSpan.FromDays(30));
            ResourceWrapper resource = new ResourceWrapper(patient, _rawResourceFactory.Create(patient, keepMeta: false), new ResourceRequest(HttpMethod.Post, "http://fhir"), false, null, null, null);
            var result = await _fhirDataStore.UpsertAsync(resource, null, true, true, CancellationToken.None);
            Assert.NotNull(result);
        }

        [Fact]
        public async Task GivenResource_WhenGetAsync_ThenDataShouldBeReturn()
        {
            await _sqlServerFhirModel.Initialize(39, true, CancellationToken.None);
            var result = await _fhirDataStore.GetAsync(new ResourceKey("Patient", "4038902", "23"), CancellationToken.None);
            Assert.NotNull(result);
        }
    }
}
