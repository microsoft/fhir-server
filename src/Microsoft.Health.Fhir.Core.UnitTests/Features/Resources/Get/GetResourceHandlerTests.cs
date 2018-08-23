// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Get;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Get
{
    public class GetResourceHandlerTests
    {
        [Fact]
        public async Task GivenAGetResourceRequest_WhenHandled_ThenTheResourceShouldBeReturned()
        {
            var repository = Substitute.For<IFhirRepository>();
            var handler = new GetResourceHandler(repository);
            string resourceId = Guid.NewGuid().ToString();
            var resource = Samples.GetDefaultObservation();
            resource.Id = resourceId;
            resource.Meta = new Meta
            {
                VersionId = Guid.NewGuid().ToString(),
            };

            repository.GetAsync(Arg.Any<ResourceKey>()).Returns(resource);

            var response = await handler.Handle(
                new GetResourceRequest(nameof(Observation), resourceId),
                CancellationToken.None);

            await repository.Received().GetAsync(Arg.Any<ResourceKey>());
            Assert.Equal(resourceId, response.Resource.Id);
        }
    }
}
