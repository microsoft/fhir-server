// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.SqlServer.Api.Features;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Messages.Get;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.Api.UnitTests.Features
{
    public class LatestSchemaVersionHandlerTests
    {
        private readonly ISchemaMigrationDataStore _schemaMigrationDataStore;
        private readonly IMediator _mediator;
        private readonly CancellationToken _cancellationToken;

        public LatestSchemaVersionHandlerTests()
        {
            _schemaMigrationDataStore = Substitute.For<ISchemaMigrationDataStore>();
            var collection = new ServiceCollection();
            collection.Add(sp => new LatestSchemaVersionHandler(_schemaMigrationDataStore)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
            _cancellationToken = new CancellationTokenSource().Token;
        }

        [Fact]
        public async Task GivenACurrentMediator_WhenCurrentRequest_ThenReturnsCurrentVersionInformation()
        {
            int mockMinVersion = 1;
            int mockMaxVersion = 3;
            _schemaMigrationDataStore.GetLatestCompatibleVersionAsync(Arg.Any<CancellationToken>())
                    .Returns(mockMaxVersion);
            GetCompatibilityVersionResponse response = await _mediator.GetCompatibleVersionAsync(mockMinVersion, _cancellationToken);

            Assert.Equal(response.Min, mockMinVersion);
            Assert.Equal(response.Max, mockMaxVersion);
        }
    }
}
