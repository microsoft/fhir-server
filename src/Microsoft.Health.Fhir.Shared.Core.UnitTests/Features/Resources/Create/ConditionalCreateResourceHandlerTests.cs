// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Create;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Resources.Create;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Create)]
[Trait(Traits.Category, Categories.ConditionalOperations)]
public class ConditionalCreateResourceHandlerTests
{
    private readonly ConditionalCreateResourceHandler _conditionalCreateHandler;
    private readonly IAuthorizationService<DataActions> _authService;
    private readonly ISearchService _searchService;
    private readonly IScopeProvider<ISearchService> _searchServiceFactory;
    private readonly IMediator _mediator;

    public ConditionalCreateResourceHandlerTests()
    {
        _authService = Substitute.For<IAuthorizationService<DataActions>>();
        IFhirDataStore fhirDataStore = Substitute.For<IFhirDataStore>();
        _searchService = Substitute.For<ISearchService>();
        _searchServiceFactory = Substitute.For<IScopeProvider<ISearchService>>();
        _mediator = Substitute.For<IMediator>();
        Lazy<IConformanceProvider> conformanceProvider = Substitute.For<Lazy<IConformanceProvider>>();
        IResourceWrapperFactory resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
        ResourceIdProvider resourceIdProvider = Substitute.For<ResourceIdProvider>();
        ILogger<ConditionalCreateResourceHandler> logger = Substitute.For<ILogger<ConditionalCreateResourceHandler>>();

        // Setup search service to return no matches (for create scenarios)
        var searchResults = SearchResult.Empty();

        _searchService.SearchAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
            Arg.Any<CancellationToken>())
            .Returns(searchResults);

        _conditionalCreateHandler = new ConditionalCreateResourceHandler(
            fhirDataStore,
            conformanceProvider,
            resourceWrapperFactory,
            _searchService,
            _mediator,
            resourceIdProvider,
            _authService,
            logger);
    }

    [Fact]
    public async Task GivenAConditionalCreateResourceHandler_WhenUserHasSearchAndCreatePermissions_ThenCreateShouldSucceed()
    {
        // Arrange
        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Create, CancellationToken.None)
            .Returns(DataActions.Search | DataActions.Create);

        var patient = Samples.GetDefaultPatient();
        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalCreateResourceRequest(patient, conditionalParameters, null);

        // Act & Assert - Should not throw UnauthorizedFhirActionException
        await _conditionalCreateHandler.Handle(request, CancellationToken.None);

        await _mediator
            .Received()
            .Send<UpsertResourceResponse>(Arg.Any<CreateResourceRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAConditionalCreateResourceHandler_WhenUserHasLegacyReadAndWritePermissions_ThenCreateShouldSucceed()
    {
        // Arrange
        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Create, CancellationToken.None)
            .Returns(DataActions.Read | DataActions.Write);

        var patient = Samples.GetDefaultPatient();
        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalCreateResourceRequest(patient, conditionalParameters, null);

        // Act & Assert - Should not throw UnauthorizedFhirActionException
        await _conditionalCreateHandler.Handle(request, CancellationToken.None);

        await _mediator
            .Received()
            .Send<UpsertResourceResponse>(Arg.Any<CreateResourceRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAConditionalCreateResourceHandler_WhenUserHasOnlySearchPermission_ThenUnauthorizedExceptionIsThrown()
    {
        // Arrange
        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Create, CancellationToken.None)
            .Returns(DataActions.Search);

        var patient = Samples.GetDefaultPatient();
        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalCreateResourceRequest(patient, conditionalParameters, null);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _conditionalCreateHandler.Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task GivenAConditionalCreateResourceHandler_WhenUserHasOnlyCreatePermission_ThenUnauthorizedExceptionIsThrown()
    {
        // Arrange
        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Create, CancellationToken.None)
            .Returns(DataActions.Create);

        var patient = Samples.GetDefaultPatient();
        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalCreateResourceRequest(patient, conditionalParameters, null);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _conditionalCreateHandler.Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task GivenAConditionalCreateResourceHandler_WhenUserHasOnlyReadPermission_ThenUnauthorizedExceptionIsThrown()
    {
        // Arrange
        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Create, CancellationToken.None)
            .Returns(DataActions.Read);

        var patient = Samples.GetDefaultPatient();
        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalCreateResourceRequest(patient, conditionalParameters, null);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _conditionalCreateHandler.Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task GivenAConditionalCreateResourceHandler_WhenUserLacksAllPermissions_ThenUnauthorizedExceptionIsThrown()
    {
        // Arrange
        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Create, CancellationToken.None)
            .Returns(DataActions.None);

        var patient = Samples.GetDefaultPatient();
        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalCreateResourceRequest(patient, conditionalParameters, null);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _conditionalCreateHandler.Handle(request, CancellationToken.None));
    }
}
