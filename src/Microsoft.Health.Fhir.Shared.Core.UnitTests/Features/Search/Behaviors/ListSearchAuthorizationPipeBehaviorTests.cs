// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Behavior;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ListSearchAuthorizationPipeBehaviorTests
    {
        [Fact]
        public async Task GivenANonEmptyListSearch_WhenUnauthorized_ThenPipelineDoesNotContinue()
        {
            var authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>())
                .Returns(DataActions.None);
            var behavior = new ListSearchAuthorizationPipeBehavior(authorizationService);
            var request = new SearchResourceRequest(
                "Patient",
                new[] { Tuple.Create(KnownQueryParameterNames.List, "list-id") });
            bool continued = false;

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(
                () => behavior.HandleAsync(
                    request,
                    () =>
                    {
                        continued = true;
                        return Task.FromResult<SearchResourceResponse>(null);
                    },
                    CancellationToken.None));

            Assert.False(continued);
            await authorizationService.Received(1).CheckAccess(
                DataActions.Search | DataActions.Read,
                CancellationToken.None);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public async Task GivenNoUsableListSearch_WhenHandled_ThenAuthorizationIsDeferredToHandler(string listId)
        {
            var authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            var behavior = new ListSearchAuthorizationPipeBehavior(authorizationService);
            IReadOnlyList<Tuple<string, string>> queries = listId == null
                ? Array.Empty<Tuple<string, string>>()
                : new[] { Tuple.Create(KnownQueryParameterNames.List, listId) };
            var request = new SearchResourceRequest("Patient", queries);
            var expected = new SearchResourceResponse(new Bundle().ToResourceElement());

            SearchResourceResponse actual = await behavior.HandleAsync(
                request,
                () => Task.FromResult(expected),
                CancellationToken.None);

            Assert.Same(expected, actual);
            await authorizationService.DidNotReceiveWithAnyArgs().CheckAccess(default, default);
        }
    }
}
