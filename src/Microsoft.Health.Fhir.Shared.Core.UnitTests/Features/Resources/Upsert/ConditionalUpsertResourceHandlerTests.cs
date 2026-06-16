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
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Upsert;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Resources.Upsert;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]

[Trait(Traits.Category, Categories.ConditionalOperations)]
public class ConditionalUpsertResourceHandlerTests
{
    private readonly ConditionalUpsertResourceHandler _conditionalUpsertHandler;
    private readonly IAuthorizationService<DataActions> _authService;
    private readonly ISearchService _searchService;
    private readonly IMediator _mediator;

    public ConditionalUpsertResourceHandlerTests()
    {
        _authService = Substitute.For<IAuthorizationService<DataActions>>();
        IFhirDataStore fhirDataStore = Substitute.For<IFhirDataStore>();
        _searchService = Substitute.For<ISearchService>();
        _mediator = Substitute.For<IMediator>();
        Lazy<IConformanceProvider> conformanceProvider = Substitute.For<Lazy<IConformanceProvider>>();
        IResourceWrapperFactory resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
        ResourceIdProvider resourceIdProvider = Substitute.For<ResourceIdProvider>();
        ILogger<ConditionalUpsertResourceHandler> logger = Substitute.For<ILogger<ConditionalUpsertResourceHandler>>();

        _conditionalUpsertHandler = new ConditionalUpsertResourceHandler(
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
    public async Task GivenAConditionalUpsertResourceHandler_WhenUserHasSearchAndUpdatePermissions_ThenUpsertShouldSucceed()
    {
        // Arrange
        // Setup search service to return one match (for upsert scenarios)
        var searchResult = GetSearchResult(Samples.GetDefaultPatient());

        var taskResult = Task.FromResult(searchResult);

        _searchService.SearchAsync(
          Arg.Any<string>(),
          Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
          Arg.Any<CancellationToken>())
          .Returns(taskResult);

        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, CancellationToken.None)
            .Returns(DataActions.Search | DataActions.Update);

        var patient = Samples.GetDefaultPatient();
        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalUpsertResourceRequest(patient, conditionalParameters, null);

        // Act & Assert - Should not throw UnauthorizedFhirActionException
        await _conditionalUpsertHandler.Handle(request, CancellationToken.None);

        await _mediator
            .Received()
            .Send<UpsertResourceResponse>(Arg.Any<UpsertResourceRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAConditionalUpsertResourceHandler_WhenUserHasLegacyReadAndWritePermissions_ThenUpsertShouldSucceed()
    {
        // Arrange
        // Setup search service to return one match (for upsert scenarios)
        var searchResult = GetSearchResult(Samples.GetDefaultPatient());

        var taskResult = Task.FromResult(searchResult);

        _searchService.SearchAsync(
          Arg.Any<string>(),
          Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
          Arg.Any<CancellationToken>())
          .Returns(taskResult);

        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, CancellationToken.None)
            .Returns(DataActions.Read | DataActions.Write);

        var patient = Samples.GetDefaultPatient();
        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalUpsertResourceRequest(patient, conditionalParameters, null);

        // Act & Assert - Should not throw UnauthorizedFhirActionException
        await _conditionalUpsertHandler.Handle(request, CancellationToken.None);

        await _mediator
            .Received()
            .Send<UpsertResourceResponse>(Arg.Any<UpsertResourceRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(DataActions.Search)]
    [InlineData(DataActions.Update)]
    [InlineData(DataActions.Read)]
    [InlineData(DataActions.None)]
    public async Task GivenAConditionalUpsertResourceHandler_WhenUserhasInsufficientPermission_ThenUnauthorizedExceptionIsThrown(DataActions returnedDataAction)
    {
        // Arrange
        // Setup search service to return one match (for upsert scenarios)
        // Setup search service to return one match (for upsert scenarios)
        var searchResult = GetSearchResult(Samples.GetDefaultPatient());

        var taskResult = Task.FromResult(searchResult);

        _searchService.SearchAsync(
          Arg.Any<string>(),
          Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
          Arg.Any<CancellationToken>())
          .Returns(taskResult);

        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, CancellationToken.None)
            .Returns(returnedDataAction);

        var patient = Samples.GetDefaultPatient();
        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalUpsertResourceRequest(patient, conditionalParameters, null);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _conditionalUpsertHandler.Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task GivenAConditionalUpsertResourceHandler_WhenNoMatchFoundAndResourceHasNoId_ThenCreateIsExecuted()
    {
        IReadOnlyCollection<SearchResultEntry> searchResults = Array.Empty<SearchResultEntry>().AsReadOnly();

        // Arrange - Setup for no matches to test create path
        // Setup search service to return no matches
        var searchResult = new SearchResult(new List<SearchResultEntry>().AsReadOnly(), null, null, Array.Empty<Tuple<string, string>>());

        var taskResult = Task.FromResult(searchResult);

        _searchService.SearchAsync(
          Arg.Any<string>(),
          Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
          Arg.Any<CancellationToken>())
          .Returns(taskResult);

        _authService
            .CheckAccess(DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, CancellationToken.None)
            .Returns(DataActions.Search | DataActions.Update);

        var patient = Samples.GetDefaultPatient().ToPoco();
        patient.Id = null; // No ID to trigger create path
        var conditionalParameters = new List<Tuple<string, string>> { new("name", "John") };
        var request = new ConditionalUpsertResourceRequest(patient.ToResourceElement(), conditionalParameters, null);

        // Act
        await _conditionalUpsertHandler.Handle(request, CancellationToken.None);

        // Assert - Should call create since no ID and no matches
        await _mediator
            .Received()
            .Send<UpsertResourceResponse>(Arg.Any<CreateResourceRequest>(), Arg.Any<CancellationToken>());
    }

    private SearchResult GetSearchResult(ResourceElement resourceElement)
    {
        var resource = resourceElement.ToPoco();
        resource.Id = "example";
        resource.VersionId = "version1";
        resource.Meta.Profile = new List<string> { "test" };
        var rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());
        ResourceElement typedElement = resource.ToResourceElement();

        var wrapper = new ResourceWrapper(typedElement, rawResourceFactory.Create(typedElement, keepMeta: true), new ResourceRequest(HttpMethod.Post, "http://fhir"), false, null, null, null);
        var result = new SearchResultEntry(wrapper, ValueSets.SearchEntryMode.Match);

        var searchResult = new SearchResult(
            Enumerable.Repeat(result, 1),
            null,
            null,
            Array.Empty<Tuple<string, string>>(),
            null,
            null);

        return searchResult;
    }
}
