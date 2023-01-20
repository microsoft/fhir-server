// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Numerics;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.ChangeFeed
{
    /// <summary>
    /// A fixture class to share a single database instance among all resource change capture tests.
    /// </summary>
    public class SqlServerFhirResourceChangeCaptureFixture : IAsyncLifetime
    {
        private IOptions<CoreFeatureConfiguration> _coreFeatureConfigOptions;
        private readonly FhirStorageTestsFixture _storageFixture;
        private readonly SqlServerFhirStorageTestsFixture _sqlFixture;
        private string _databaseName;

        public SqlServerFhirResourceChangeCaptureFixture()
        {
            _databaseName = $"FHIRRESOURCECHANGEINTTEST_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{BigInteger.Abs(new BigInteger(Guid.NewGuid().ToByteArray()))}";
            _coreFeatureConfigOptions = Options.Create(new CoreFeatureConfiguration() { SupportsResourceChangeCapture = true });
            _sqlFixture = new SqlServerFhirStorageTestsFixture(SchemaVersionConstants.Max, _databaseName, _coreFeatureConfigOptions);
            _storageFixture = new FhirStorageTestsFixture(_sqlFixture);
        }

        public Mediator Mediator => _storageFixture.Mediator;

        public SqlConnectionWrapperFactory SqlConnectionWrapperFactory => _sqlFixture.SqlConnectionWrapperFactory;

        public SchemaInformation SchemaInformation => _sqlFixture.SchemaInformation;

        public async Task InitializeAsync()
        {
            await _storageFixture.InitializeAsync();
        }

        public async Task DisposeAsync()
        {
            await (_storageFixture?.DisposeAsync() ?? Task.CompletedTask);
        }
    }
}
