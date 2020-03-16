// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
    public class CurrentVersionSchemaHandlerTests
    {
        private readonly ISchemaMigrationDataStore _schemaMigrationDataStore;
        private readonly IMediator _mediator;
        private readonly CancellationToken _cancellationToken;

        public CurrentVersionSchemaHandlerTests()
        {
            _schemaMigrationDataStore = Substitute.For<ISchemaMigrationDataStore>();
            var collection = new ServiceCollection();
            collection.Add(sp => new CurrentSchemaVersionHandler(_schemaMigrationDataStore)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
            _cancellationToken = new CancellationTokenSource().Token;
        }

        [Fact]
        public async Task GivenACurrentMediator_WhenCurrentRequest_ThenReturnsCurrentVersionInformation()
        {
            IList<CurrentVersionInformation> mockCurrentVersions = CurrentVersions();
            SetupDataStore(_ => new GetCurrentVersionResponse(mockCurrentVersions));
            GetCurrentVersionResponse response = await _mediator.GetCurrentVersionAsync(_cancellationToken);

            Assert.Equal(mockCurrentVersions.Count, response.CurrentVersions.Count);

            void SetupDataStore(Func<NSubstitute.Core.CallInfo, GetCurrentVersionResponse> returnThis)
            {
                _schemaMigrationDataStore.GetCurrentVersionAsync(Arg.Any<CancellationToken>())
                    .Returns(returnThis);
            }
        }

        private IList<CurrentVersionInformation> CurrentVersions()
        {
            IList<CurrentVersionInformation> currentVersions = new List<CurrentVersionInformation>();

            currentVersions.Add(new CurrentVersionInformation(1, "complete", new List<string>() { "server1", "server2" }));
            currentVersions.Add(new CurrentVersionInformation(2, "complete", new List<string>()));

            return currentVersions;
        }
    }
}
