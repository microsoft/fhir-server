// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Security.Authorization
{
    /// <summary>
    /// Verifies the SMART compartment ownership check applied to by-id writes.
    /// An out-of-compartment target must surface as <see cref="ResourceNotFoundException"/> (404) rather than
    /// <see cref="UnauthorizedFhirActionException"/> (403); a 403 would confirm the resource exists and hand
    /// back the enumeration oracle this check exists to close. GetResourceHandler already returns 404 for the
    /// same condition, so writes must agree with it or the difference itself leaks existence.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Authorization)]
    public class SmartCompartmentResourceValidatorTests
    {
        private const string ResourceType = "Observation";
        private const string ResourceId = "out-of-compartment-id";

        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly IFhirDataStore _fhirDataStore = Substitute.For<IFhirDataStore>();
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

        public SmartCompartmentResourceValidatorTests()
        {
            // The update-as-create path builds a ResourceKey, which resolves the resource type through the provider.
            ModelInfoProvider.SetProvider(MockModelInfoProviderBuilder.Create(FhirSpecification.R4).Build());

            SetFineGrainedAccessControl(true);
            SetSearchResult(Array.Empty<SearchResultEntry>());
        }

        [Fact]
        public async Task GivenFineGrainedAccessControlIsDisabled_WhenValidating_ThenNoCompartmentSearchIsPerformed()
        {
            SetFineGrainedAccessControl(false);

            await SmartCompartmentResourceValidator.EnsureResourceIsInCompartmentAsync(
                _searchService, _contextAccessor, ResourceType, ResourceId, CancellationToken.None);

            await _searchService.DidNotReceive().SearchAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenNoRequestContext_WhenValidating_ThenNoCompartmentSearchIsPerformed()
        {
            _contextAccessor.RequestContext.Returns((IFhirRequestContext)null);

            await SmartCompartmentResourceValidator.EnsureResourceIsInCompartmentAsync(
                _searchService, _contextAccessor, ResourceType, ResourceId, CancellationToken.None);

            await _searchService.DidNotReceive().SearchAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenResourceIsInTheCallersCompartment_WhenValidating_ThenNoExceptionIsThrown()
        {
            SetSearchResult(new[] { new SearchResultEntry(CreateWrapper(deleted: false)) });

            await SmartCompartmentResourceValidator.EnsureResourceIsInCompartmentAsync(
                _searchService, _contextAccessor, ResourceType, ResourceId, CancellationToken.None);
        }

        [Fact]
        public async Task GivenResourceIsOutsideTheCallersCompartment_WhenValidating_ThenResourceNotFoundExceptionIsThrown()
        {
            await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                SmartCompartmentResourceValidator.EnsureResourceIsInCompartmentAsync(
                    _searchService, _contextAccessor, ResourceType, ResourceId, CancellationToken.None));
        }

        [Fact]
        public async Task GivenResourceIsOutsideTheCallersCompartment_WhenValidating_ThenUnauthorizedIsNotThrown()
        {
            // Pins the 404 contract: a 403 here would confirm the resource exists.
            Exception thrown = await Record.ExceptionAsync(() =>
                SmartCompartmentResourceValidator.EnsureResourceIsInCompartmentAsync(
                    _searchService, _contextAccessor, ResourceType, ResourceId, CancellationToken.None));

            Assert.IsNotType<UnauthorizedFhirActionException>(thrown);
        }

        [Fact]
        public async Task GivenResourceExistsOutsideTheCallersCompartment_WhenUpdating_ThenResourceNotFoundExceptionIsThrown()
        {
            _fhirDataStore.GetAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>())
                .Returns(CreateWrapper(deleted: false));

            await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                SmartCompartmentResourceValidator.EnsureResourceIsInCompartmentAsync(
                    _searchService, _contextAccessor, ResourceType, ResourceId, CancellationToken.None, _fhirDataStore));
        }

        [Fact]
        public async Task GivenResourceDoesNotExistAtAll_WhenUpdating_ThenUpdateAsCreateIsAllowed()
        {
            _fhirDataStore.GetAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>())
                .Returns((ResourceWrapper)null);

            await SmartCompartmentResourceValidator.EnsureResourceIsInCompartmentAsync(
                _searchService, _contextAccessor, ResourceType, ResourceId, CancellationToken.None, _fhirDataStore);
        }

        [Fact]
        public async Task GivenResourceIsDeleted_WhenUpdating_ThenUpdateAsCreateIsAllowed()
        {
            _fhirDataStore.GetAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>())
                .Returns(CreateWrapper(deleted: true));

            await SmartCompartmentResourceValidator.EnsureResourceIsInCompartmentAsync(
                _searchService, _contextAccessor, ResourceType, ResourceId, CancellationToken.None, _fhirDataStore);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public async Task GivenNoResourceId_WhenValidating_ThenNoCompartmentSearchIsPerformed(string resourceId)
        {
            await SmartCompartmentResourceValidator.EnsureResourceIsInCompartmentAsync(
                _searchService, _contextAccessor, ResourceType, resourceId, CancellationToken.None);

            await _searchService.DidNotReceive().SearchAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAByIdWrite_WhenValidating_ThenTheCompartmentSearchIsScopedToThatResource()
        {
            SetSearchResult(new[] { new SearchResultEntry(CreateWrapper(deleted: false)) });

            await SmartCompartmentResourceValidator.EnsureResourceIsInCompartmentAsync(
                _searchService, _contextAccessor, ResourceType, ResourceId, CancellationToken.None);

            await _searchService.Received(1).SearchAsync(
                ResourceType,
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(q =>
                    q.Count == 1 && q[0].Item1 == KnownQueryParameterNames.Id && q[0].Item2 == ResourceId),
                Arg.Any<CancellationToken>());
        }

        private void SetFineGrainedAccessControl(bool enabled)
        {
            var context = Substitute.For<IFhirRequestContext>();
            context.AccessControlContext.Returns(new AccessControlContext { ApplyFineGrainedAccessControl = enabled });
            _contextAccessor.RequestContext.Returns(context);
        }

        private void SetSearchResult(IEnumerable<SearchResultEntry> entries)
        {
            _searchService.SearchAsync(
                    Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>())
                .Returns(new SearchResult(entries.ToList(), null, null, Array.Empty<Tuple<string, string>>()));
        }

        private static ResourceWrapper CreateWrapper(bool deleted) => new ResourceWrapper(
            ResourceId,
            "1",
            ResourceType,
            new RawResource("{}", FhirResourceFormat.Json, false),
            new ResourceRequest(HttpMethod.Put),
            DateTimeOffset.UtcNow,
            deleted,
            null,
            null,
            null);
    }
}
