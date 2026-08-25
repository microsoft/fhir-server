// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Resources.Patch;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Patch)]
[Trait(Traits.Category, Categories.ConditionalOperations)]
public class ConditionalPatchResourceHandlerTests
{
    private readonly ConditionalPatchResourceHandler _conditionalPatchHandler;
    private readonly IAuthorizationService<DataActions> _authService;
    private readonly ISearchService _searchService;
    private readonly IMediator _mediator;

    public ConditionalPatchResourceHandlerTests()
    {
        _authService = Substitute.For<IAuthorizationService<DataActions>>();
        IFhirDataStore fhirDataStore = Substitute.For<IFhirDataStore>();
        _searchService = Substitute.For<ISearchService>();
        _mediator = Substitute.For<IMediator>();
        Lazy<IConformanceProvider> conformanceProvider = Substitute.For<Lazy<IConformanceProvider>>();
        IResourceWrapperFactory resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
        ResourceIdProvider resourceIdProvider = Substitute.For<ResourceIdProvider>();
        ILogger<ConditionalPatchResourceHandler> logger = Substitute.For<ILogger<ConditionalPatchResourceHandler>>();

        _conditionalPatchHandler = new ConditionalPatchResourceHandler(
            fhirDataStore,
            conformanceProvider,
            resourceWrapperFactory,
            _searchService,
            _mediator,
            resourceIdProvider,
            _authService,
            logger);
        var searchResult = new SearchResult(
            GenerateSearchResult("Patient"),
            null,
            null,
            Array.Empty<Tuple<string, string>>(),
            null,
            null);

        _searchService.SearchAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
            Arg.Any<CancellationToken>())
            .Returns(searchResult);
    }

    [Fact]
    public async Task GivenAConditionalPatchResourceHandler_WhenUserHasSearchAndUpdatePermissions_ThenPatchShouldSucceed()
    {
        // Arrange
        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, CancellationToken.None)
            .Returns(DataActions.Search | DataActions.Update);

        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalPatchResourceRequest("Patient", new FhirPathPatchPayload(new Parameters()), conditionalParameters, null);

        // Act & Assert - Should not throw UnauthorizedFhirActionException
        await _conditionalPatchHandler.HandleAsync(request, CancellationToken.None);

        await _mediator
            .Received()
            .SendAsync<UpsertResourceResponse>(Arg.Any<UpsertResourceRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAConditionalPatchResourceHandler_WhenUserHasLegacyReadAndWritePermissions_ThenPatchShouldSucceed()
    {
        // Arrange
        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, CancellationToken.None)
            .Returns(DataActions.Read | DataActions.Write);

        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalPatchResourceRequest("Patient", new FhirPathPatchPayload(new Parameters()), conditionalParameters, null);

        // Act & Assert - Should not throw UnauthorizedFhirActionException
        await _conditionalPatchHandler.HandleAsync(request, CancellationToken.None);

        await _mediator
            .Received()
            .SendAsync<UpsertResourceResponse>(Arg.Any<UpsertResourceRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAConditionalPatchResourceHandler_WhenUserHasOnlySearchPermission_ThenUnauthorizedExceptionIsThrown()
    {
        // Arrange
        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, CancellationToken.None)
            .Returns(DataActions.Search);

        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalPatchResourceRequest("Patient", new FhirPathPatchPayload(new Parameters()), conditionalParameters, null);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _conditionalPatchHandler.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task GivenAConditionalPatchResourceHandler_WhenUserHasOnlyUpdatePermission_ThenUnauthorizedExceptionIsThrown()
    {
        // Arrange
        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, CancellationToken.None)
            .Returns(DataActions.Update);

        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalPatchResourceRequest("Patient", new FhirPathPatchPayload(new Parameters()), conditionalParameters, null);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _conditionalPatchHandler.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task GivenAConditionalPatchResourceHandler_WhenUserHasOnlyReadPermission_ThenUnauthorizedExceptionIsThrown()
    {
        // Arrange
        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, CancellationToken.None)
            .Returns(DataActions.Read);

        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalPatchResourceRequest("Patient", new FhirPathPatchPayload(new Parameters()), conditionalParameters, null);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _conditionalPatchHandler.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task GivenAConditionalPatchResourceHandler_WhenUserLacksAllPermissions_ThenUnauthorizedExceptionIsThrown()
    {
        // Arrange
        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, CancellationToken.None)
            .Returns(DataActions.None);

        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalPatchResourceRequest("Patient", new FhirPathPatchPayload(new Parameters()), conditionalParameters, null);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _conditionalPatchHandler.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task GivenAConditionalPatchResourceHandler_WhenRequestContainsAMatchingWeakETag_ThenPatchShouldSucceed()
    {
        // Arrange
        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, CancellationToken.None)
            .Returns(DataActions.Search | DataActions.Update);

        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var weakETag = WeakETag.FromVersionId("1");
        var request = new ConditionalPatchResourceRequest("Patient", new FhirPathPatchPayload(new Parameters()), conditionalParameters, weakETag: weakETag);

        // Act & Assert - Should not throw PreconditionFailedException
        await _conditionalPatchHandler.HandleAsync(request, CancellationToken.None);

        await _mediator
            .Received()
            .SendAsync<UpsertResourceResponse>(
                Arg.Is<UpsertResourceRequest>(r => r.WeakETag == weakETag && r.WeakETag.VersionId == "1"),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAConditionalPatchResourceHandler_WhenRequestContainsANonMatchingWeakETag_ThenPreconditionFailedExceptionIsThrown()
    {
        // Arrange
        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, CancellationToken.None)
            .Returns(DataActions.Search | DataActions.Update);

        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var weakETag = WeakETag.FromVersionId("2");
        var request = new ConditionalPatchResourceRequest("Patient", new FhirPathPatchPayload(new Parameters()), conditionalParameters, weakETag: weakETag);

        // Act & Assert
        await Assert.ThrowsAsync<PreconditionFailedException>(() => _conditionalPatchHandler.HandleAsync(request, CancellationToken.None));

        await _mediator
            .DidNotReceive()
            .SendAsync<UpsertResourceResponse>(Arg.Any<UpsertResourceRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAConditionalPatchResourceHandler_WhenTheMatchHasAVersion_ThenTheSearchVersionIsForwardedAsComparedVersion()
    {
        // Arrange
        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, CancellationToken.None)
            .Returns(DataActions.Search | DataActions.Update);

        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalPatchResourceRequest("Patient", new FhirPathPatchPayload(new Parameters()), conditionalParameters, null);

        // Act
        await _conditionalPatchHandler.HandleAsync(request, CancellationToken.None);

        // Assert: with no client header the write still carries the authoritative search-to-write CAS guard.
        await _mediator
            .Received()
            .SendAsync<UpsertResourceResponse>(
                Arg.Is<UpsertResourceRequest>(r => r.WeakETag == null && r.ComparedVersion == "1"),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAConditionalPatchResourceHandler_WhenRequestContainsAMatchingWeakETag_ThenTheClientETagAndComparedVersionAreBothForwarded()
    {
        // Arrange
        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, CancellationToken.None)
            .Returns(DataActions.Search | DataActions.Update);

        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var weakETag = WeakETag.FromVersionId("1");
        var request = new ConditionalPatchResourceRequest("Patient", new FhirPathPatchPayload(new Parameters()), conditionalParameters, weakETag: weakETag);

        // Act
        await _conditionalPatchHandler.HandleAsync(request, CancellationToken.None);

        // Assert: the client tag is preserved unchanged and the internal guard is added alongside it.
        await _mediator
            .Received()
            .SendAsync<UpsertResourceResponse>(
                Arg.Is<UpsertResourceRequest>(r => r.WeakETag == weakETag && r.ComparedVersion == "1"),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAConditionalPatchResourceHandler_WhenTheMatchVersionIsUnavailable_ThenPreconditionFailedExceptionIsThrownWithoutUpsert()
    {
        // Arrange
        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, CancellationToken.None)
            .Returns(DataActions.Search | DataActions.Update);

        _searchService.SearchAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
            Arg.Any<CancellationToken>())
            .Returns(new SearchResult(
                GenerateSearchResult("Patient", versionId: null),
                null,
                null,
                Array.Empty<Tuple<string, string>>(),
                null,
                null));

        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalPatchResourceRequest("Patient", new FhirPathPatchPayload(new Parameters()), conditionalParameters, null);

        // Act & Assert: without a version the search-to-write guard cannot be built, so fail closed.
        await Assert.ThrowsAsync<PreconditionFailedException>(() => _conditionalPatchHandler.HandleAsync(request, CancellationToken.None));

        await _mediator
            .DidNotReceive()
            .SendAsync<UpsertResourceResponse>(Arg.Any<UpsertResourceRequest>(), Arg.Any<CancellationToken>());
    }

    private static IReadOnlyCollection<SearchResultEntry> GenerateSearchResult(string resourceType, string versionId = "1")
    {
        var entries = new List<SearchResultEntry>();
        Resource resource;
        switch (resourceType)
        {
            case "Patient":
                resource = Samples.GetDefaultPatient().ToPoco<Patient>();
                break;
            case "Observation":
                resource = Samples.GetDefaultObservation().ToPoco<Observation>();
                break;
            case "Practitioner":
                resource = Samples.GetDefaultPractitioner().ToPoco<Practitioner>();
                break;
            case "Organization":
                resource = Samples.GetDefaultOrganization().ToPoco<Organization>();
                break;
            default:
                throw new ArgumentException($"Unsupported resource type: {resourceType}");
        }

        resource.Id = Guid.NewGuid().ToString();
        resource.VersionId = versionId;

        var resourceElement = resource.ToResourceElement();
        var rawResource = new RawResource(resource.ToJson(), FhirResourceFormat.Json, isMetaSet: false);
        var resourceRequest = Substitute.For<ResourceRequest>();
        var compartmentIndices = Substitute.For<CompartmentIndices>();
        var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, null, compartmentIndices, new List<KeyValuePair<string, string>>(), "hash") { IsHistory = false };
        var entry = new SearchResultEntry(wrapper, resourceType == "Organization" ? SearchEntryMode.Include : SearchEntryMode.Match);
        entries.Add(entry);
        return entries;
    }
}
