// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Numerics;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.ChangeFeed
{
    /// <summary>
    /// A fixture class to share a single database instance among all resource change capture tests.
    /// </summary>
    public class SqlServerFhirResourceChangeCaptureFixture : IDisposable
    {
        private IOptions<CoreFeatureConfiguration> _coreFeatureConfigOptions;
        private readonly FhirStorageTestsFixture _fixture;
        private string _databaseName;

        public SqlServerFhirResourceChangeCaptureFixture()
        {
            _databaseName = $"FHIRRESOURCECHANGEINTTEST_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{BigInteger.Abs(new BigInteger(Guid.NewGuid().ToByteArray()))}";
            _coreFeatureConfigOptions = Options.Create(new CoreFeatureConfiguration() { SupportsResourceChangeCapture = true });
            _fixture = new FhirStorageTestsFixture(new SqlServerFhirStorageTestsFixture(SchemaVersionConstants.Max, _databaseName, _coreFeatureConfigOptions));
            _fixture.InitializeAsync().Wait();

            Mediator = _fixture.Mediator;
            DatabaseName = _databaseName;
        }

        public string DatabaseName { get; private set; }

        public Mediator Mediator { get; private set; }

        public async void Dispose()
        {
            await _fixture?.DisposeAsync();
        }
    }
}
