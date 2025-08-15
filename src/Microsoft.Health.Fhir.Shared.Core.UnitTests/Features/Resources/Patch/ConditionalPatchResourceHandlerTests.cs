// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
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
            _searchService,
            fhirDataStore,
            conformanceProvider,
            resourceWrapperFactory,
            resourceIdProvider,
            _authService,
            _mediator,
            logger);

        // Setup search service to return one match
        var searchResults = SearchResult.Empty();
        searchResults.Results = new List<SearchResultEntry>
        {
            new SearchResultEntry(Samples.GetDefaultPatient(), SearchEntryMode.Match)
        };
        
        _searchService.ConditionalSearchAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<ILogger>())
            .Returns(searchResults);
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
        await _conditionalPatchHandler.Handle(request, CancellationToken.None);

        await _mediator
            .Received()
            .Send<UpsertResourceResponse>(Arg.Any<UpsertResourceRequest>(), Arg.Any<CancellationToken>());
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
        await _conditionalPatchHandler.Handle(request, CancellationToken.None);

        await _mediator
            .Received()
            .Send<UpsertResourceResponse>(Arg.Any<UpsertResourceRequest>(), Arg.Any<CancellationToken>());
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
        await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _conditionalPatchHandler.Handle(request, CancellationToken.None));
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
        await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _conditionalPatchHandler.Handle(request, CancellationToken.None));
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
        await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _conditionalPatchHandler.Handle(request, CancellationToken.None));
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
        await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _conditionalPatchHandler.Handle(request, CancellationToken.None));
    }
}
