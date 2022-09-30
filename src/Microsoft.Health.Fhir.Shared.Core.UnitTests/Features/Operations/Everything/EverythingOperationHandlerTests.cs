// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Everything;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Everything;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Everything
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class EverythingOperationHandlerTests
    {
        private readonly IPatientEverythingService _patientEverythingService = Substitute.For<IPatientEverythingService>();
        private readonly IBundleFactory _bundleFactory = Substitute.For<IBundleFactory>();

        private readonly EverythingOperationHandler _everythingOperationHandler;

        public EverythingOperationHandlerTests()
        {
            _everythingOperationHandler = new EverythingOperationHandler(_patientEverythingService, _bundleFactory, DisabledFhirAuthorizationService.Instance);
        }

        [Fact]
        public async Task GivenAnEverythingOperationRequest_WhenHandled_ThenABundleShouldBeReturned()
        {
            var request = new EverythingOperationRequest("Patient", "123");

            var searchResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);

            _patientEverythingService.SearchAsync(
                request.ResourceId,
                request.Start,
                request.End,
                request.Since,
                request.ResourceTypes,
                request.ContinuationToken,
                CancellationToken.None).Returns(searchResult);

            var expectedBundle = new Bundle().ToResourceElement();

            _bundleFactory.CreateSearchBundle(searchResult).Returns(expectedBundle);

            EverythingOperationResponse actualResponse = await _everythingOperationHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(actualResponse);
            Assert.Equal(expectedBundle, actualResponse.Bundle);
        }
    }
}
