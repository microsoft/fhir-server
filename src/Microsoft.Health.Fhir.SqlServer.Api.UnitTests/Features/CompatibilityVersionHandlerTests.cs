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
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.Api.UnitTests.Features
{
    public class CompatibilityVersionHandlerTests
    {
        private readonly ISchemaDataStore _schemaMigrationDataStore;
        private readonly IMediator _mediator;
        private readonly CancellationToken _cancellationToken;

        public CompatibilityVersionHandlerTests()
        {
            _schemaMigrationDataStore = Substitute.For<ISchemaDataStore>();
            var collection = new ServiceCollection();
            collection.Add(sp => new CompatibilityVersionHandler(_schemaMigrationDataStore)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
            _cancellationToken = new CancellationTokenSource().Token;
        }

        [Fact]
        public async Task GivenAMediator_WhenCompatibleRequest_ThenReturnsCompatibleVersions()
        {
            _schemaMigrationDataStore.GetLatestCompatibleVersionAsync(Arg.Any<CancellationToken>())
                    .Returns(new GetCompatibilityVersionResponse(new CompatibleVersions(1, 3)));
            GetCompatibilityVersionResponse response = await _mediator.GetCompatibleVersionAsync(_cancellationToken);

            Assert.Equal(1, response.Versions.Min);
            Assert.Equal(3, response.Versions.Max);
        }
    }
}
