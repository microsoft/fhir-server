// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Create;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Create
{
    public class CreateResourceHandlerTests
    {
        [Fact]
        public async Task GivenACreateResourceRequest_WhenHandled_ThenTheResourceShouldBeUpsertedIntoTheRepository()
        {
            var repository = Substitute.For<IFhirRepository>();
            var handler = new CreateResourceHandler(repository);
            var observation = Samples.GetDefaultObservation();

            repository.CreateAsync(Arg.Any<Resource>()).Returns(observation);

            await handler.Handle(new CreateResourceRequest(observation), CancellationToken.None);

            await repository.Received().CreateAsync(Arg.Is(observation));
        }
    }
}
