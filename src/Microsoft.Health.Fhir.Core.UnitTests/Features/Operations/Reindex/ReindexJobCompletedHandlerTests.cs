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
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.Reindex;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Reindex
{
    public class ReindexJobCompletedHandlerTests
    {
        private readonly ISupportedSearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISupportedSearchParameterDefinitionManager>();
        private readonly ISearchParameterRegistry _searchParameterRegistry = Substitute.For<ISearchParameterRegistry>();
        private readonly IMediator _mediator;

        public ReindexJobCompletedHandlerTests()
        {
            var collection = new ServiceCollection();
            collection.Add(x => new ReindexJobCompletedHandler(_searchParameterDefinitionManager, _searchParameterRegistry)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenHandlingAReindexJobCompletedRequest_ThenResultShouldBeSuccess()
        {
            var paramUris = new List<string>();
            paramUris.Add("http://searchParam");

            var reqeuest = new ReindexJobCompletedRequest(paramUris);
            var result = await _mediator.Send(reqeuest, CancellationToken.None);

            Assert.True(result.Success);
        }

        [Fact]
        public async Task GivenASupportedSearchParamNotSupportedException_WhenHandlingAReindexJobCompletedRequest_ThenResultShouldBeFalse()
        {
            var paramUris = new List<string>();
            paramUris.Add("http://searchParam");

            _searchParameterDefinitionManager.When(s => s.SetSearchParameterEnabled(Arg.Any<Uri>()))
                .Do(e => throw new SearchParameterNotSupportedException("message"));

            var reqeuest = new ReindexJobCompletedRequest(paramUris);
            var result = await _mediator.Send(reqeuest, CancellationToken.None);

            Assert.False(result.Success);
        }
    }
}
