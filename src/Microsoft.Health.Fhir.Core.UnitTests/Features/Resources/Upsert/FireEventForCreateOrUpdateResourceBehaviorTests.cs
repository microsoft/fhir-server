// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Upsert;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Upsert
{
    public class FireEventForCreateOrUpdateResourceBehaviorTests
    {
        private readonly IMediator _mediator;
        private readonly FireEventForCreateOrUpdateResourceBehavior _behavior;
        private readonly RequestHandlerDelegate<UpsertResourceResponse> _requestHandlerDelegate;
        private readonly Observation _observation;

        public FireEventForCreateOrUpdateResourceBehaviorTests()
        {
            _mediator = Substitute.For<IMediator>();
            _behavior = new FireEventForCreateOrUpdateResourceBehavior(_mediator);
            _requestHandlerDelegate = Substitute.For<RequestHandlerDelegate<UpsertResourceResponse>>();
            _observation = Samples.GetDefaultObservation();

            _requestHandlerDelegate().Returns(new UpsertResourceResponse(new SaveOutcome(_observation, SaveOutcomeType.Created)));
        }

        [Fact]
        public async Task GivenAResource_WhenCreate_ThenAnUpsertedEventShouldBeFired()
        {
            await _behavior.Handle(
                new CreateResourceRequest(
                    _observation),
                CancellationToken.None,
                _requestHandlerDelegate);

            await _mediator.Received().Publish(Arg.Any<ResourceUpsertedEvent>());
        }

        [Fact]
        public async Task GivenAResource_WhenUpdated_ThenAnUpsertedEventShouldBeFired()
        {
            await _behavior.Handle(
                new UpsertResourceRequest(
                    _observation),
                CancellationToken.None,
                _requestHandlerDelegate);

            await _mediator.Received().Publish(Arg.Any<ResourceUpsertedEvent>());
        }
    }
}
