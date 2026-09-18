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
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Get;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Filters;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Resources.Get;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Get)]
public class GetResourceHandlerTests
{
    private readonly IFhirDataStore _fhirDataStore;
    private readonly Lazy<IConformanceProvider> _conformanceProvider;
    private readonly IResourceWrapperFactory _resourceWrapperFactory;
    private readonly ResourceIdProvider _resourceIdProvider;
    private readonly IDataResourceFilter _dataResourceFilter;
    private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
    private readonly ISearchService _searchService;

    public GetResourceHandlerTests()
    {
        _fhirDataStore = Substitute.For<IFhirDataStore>();
        _conformanceProvider = Substitute.For<Lazy<IConformanceProvider>>();
        _resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
        _resourceIdProvider = Substitute.For<ResourceIdProvider>();
        _dataResourceFilter = new DataResourceFilter(MissingDataFilterCriteria.Default);
        _contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        _searchService = Substitute.For<ISearchService>();

        // Setup default behavior for data store
        var patient = Samples.GetDefaultPatient();
        var rawResource = new RawResource(patient.ToPoco<Patient>().ToJson(), FhirResourceFormat.Json, false);
        var wrapper = new ResourceWrapper(
            patient,
            rawResource,
            new ResourceRequest(System.Net.Http.HttpMethod.Get),
            false,
            null,
            null,
            null);

        _fhirDataStore.GetAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>())
            .Returns(wrapper);

        // Setup context accessor default behavior
        _contextAccessor.RequestContext.Returns(Substitute.For<IFhirRequestContext>());
        _contextAccessor.RequestContext.AccessControlContext.Returns((AccessControlContext)null);
    }

    [Fact]
    public async Task GivenAGetResourceRequest_WhenUserHasReadPermission_ThenGetShouldSucceed()
    {
        // Arrange
        var authService = Substitute.For<IAuthorizationService<DataActions>>();
        var getResourceHandler = new GetResourceHandler(
            _fhirDataStore,
            _conformanceProvider,
            _resourceWrapperFactory,
            _resourceIdProvider,
            _dataResourceFilter,
            authService,
            _contextAccessor,
            _searchService);

        authService
            .CheckAccess(DataActions.Read | DataActions.ReadById, CancellationToken.None)
            .Returns(DataActions.Read);

        var request = new GetResourceRequest(new ResourceKey("Patient", "123"), bundleResourceContext: null);

        // Act & Assert - Should not throw UnauthorizedFhirActionException
        var result = await getResourceHandler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Resource);
    }

    [Fact]
    public async Task GivenAGetResourceRequest_WhenUserHasReadByIdPermission_ThenGetShouldSucceed()
    {
        // Arrange
        var authService = Substitute.For<IAuthorizationService<DataActions>>();
        var getResourceHandler = new GetResourceHandler(
            _fhirDataStore,
            _conformanceProvider,
            _resourceWrapperFactory,
            _resourceIdProvider,
            _dataResourceFilter,
            authService,
            _contextAccessor,
            _searchService);

        authService
            .CheckAccess(DataActions.Read | DataActions.ReadById, CancellationToken.None)
            .Returns(DataActions.ReadById);

        var request = new GetResourceRequest(new ResourceKey("Patient", "123"), bundleResourceContext: null);

        // Act & Assert - Should not throw UnauthorizedFhirActionException
        var result = await getResourceHandler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Resource);
    }

    [Fact]
    public async Task GivenAGetResourceRequest_WhenUserLacksPermissions_ThenUnauthorizedExceptionIsThrown()
    {
        // Arrange
        var authService = Substitute.For<IAuthorizationService<DataActions>>();
        var getResourceHandler = new GetResourceHandler(
            _fhirDataStore,
            _conformanceProvider,
            _resourceWrapperFactory,
            _resourceIdProvider,
            _dataResourceFilter,
            authService,
            _contextAccessor,
            _searchService);

        authService
            .CheckAccess(DataActions.Read | DataActions.ReadById, CancellationToken.None)
            .Returns(DataActions.None);

        var request = new GetResourceRequest(new ResourceKey("Patient", "123"), bundleResourceContext: null);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() =>
            getResourceHandler.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task GivenAGetResourceRequest_WhenUserHasOnlyWritePermission_ThenUnauthorizedExceptionIsThrown()
    {
        // Arrange
        var authService = Substitute.For<IAuthorizationService<DataActions>>();
        var getResourceHandler = new GetResourceHandler(
            _fhirDataStore,
            _conformanceProvider,
            _resourceWrapperFactory,
            _resourceIdProvider,
            _dataResourceFilter,
            authService,
            _contextAccessor,
            _searchService);

        authService
            .CheckAccess(DataActions.Read | DataActions.ReadById, CancellationToken.None)
            .Returns(DataActions.Write);

        var request = new GetResourceRequest(new ResourceKey("Patient", "123"), bundleResourceContext: null);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() =>
            getResourceHandler.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task GivenAGetResourceRequest_WhenUserHasOnlySearchPermission_ThenUnauthorizedExceptionIsThrown()
    {
        // Arrange
        var authService = Substitute.For<IAuthorizationService<DataActions>>();
        var getResourceHandler = new GetResourceHandler(
            _fhirDataStore,
            _conformanceProvider,
            _resourceWrapperFactory,
            _resourceIdProvider,
            _dataResourceFilter,
            authService,
            _contextAccessor,
            _searchService);

        authService
            .CheckAccess(DataActions.Read | DataActions.ReadById, CancellationToken.None)
            .Returns(DataActions.Search);

        var request = new GetResourceRequest(new ResourceKey("Patient", "123"), bundleResourceContext: null);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() =>
            getResourceHandler.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task GivenAGetResourceRequest_WhenResourceNotFound_ThenResourceNotFoundExceptionIsThrown()
    {
        // Arrange
        var authService = Substitute.For<IAuthorizationService<DataActions>>();
        var getResourceHandler = new GetResourceHandler(
            _fhirDataStore,
            _conformanceProvider,
            _resourceWrapperFactory,
            _resourceIdProvider,
            _dataResourceFilter,
            authService,
            _contextAccessor,
            _searchService);

        authService
            .CheckAccess(DataActions.Read | DataActions.ReadById, CancellationToken.None)
            .Returns(DataActions.Read);

        // Setup data store to return null (resource not found)
        _fhirDataStore.GetAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>())
            .Returns((ResourceWrapper)null);

        var request = new GetResourceRequest(new ResourceKey("Patient", "notfound"), bundleResourceContext: null);

        // Act & Assert
        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            getResourceHandler.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task GivenASmartGetResourceRequest_WhenSearchReturnsADifferentResource_ThenResourceNotFoundExceptionIsThrown()
    {
        // A SMART read is answered by an authorization-filtered search. If the _id constraint is dropped from
        // that search (for example because the _id search parameter is unavailable), the search still returns
        // resources the caller may see. Returning one of those for GET /Patient/requested-id would answer a read
        // with the wrong resource, so the handler must reject anything that is not the requested key.
        var getResourceHandler = CreateSmartHandler(out ISearchService searchService);

        searchService
            .SearchAsync("Patient", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>())
            .Returns(CreateSearchResult("some-other-patient"));

        var request = new GetResourceRequest(new ResourceKey("Patient", "requested-id"), bundleResourceContext: null);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            getResourceHandler.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task GivenASmartGetResourceRequest_WhenSearchReturnsTheRequestedResourceAmongOthers_ThenThatResourceIsReturned()
    {
        var getResourceHandler = CreateSmartHandler(out ISearchService searchService);

        searchService
            .SearchAsync("Patient", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>())
            .Returns(CreateSearchResult("some-other-patient", "requested-id"));

        var request = new GetResourceRequest(new ResourceKey("Patient", "requested-id"), bundleResourceContext: null);

        GetResourceResponse result = await getResourceHandler.HandleAsync(request, CancellationToken.None);

        Assert.Equal("requested-id", result.Resource.Id);
    }

    [Fact]
    public async Task GivenASmartGetResourceRequest_WhenSearchReturnsNoResults_ThenResourceNotFoundExceptionIsThrown()
    {
        var getResourceHandler = CreateSmartHandler(out ISearchService searchService);

        searchService
            .SearchAsync("Patient", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>())
            .Returns(CreateSearchResult());

        var request = new GetResourceRequest(new ResourceKey("Patient", "requested-id"), bundleResourceContext: null);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            getResourceHandler.HandleAsync(request, CancellationToken.None));
    }

    private GetResourceHandler CreateSmartHandler(out ISearchService searchService)
    {
        var authService = Substitute.For<IAuthorizationService<DataActions>>();
        authService
            .CheckAccess(DataActions.Read | DataActions.ReadById, CancellationToken.None)
            .Returns(DataActions.Read);

        var accessControlContext = new AccessControlContext { ApplyFineGrainedAccessControl = true };
        var requestContext = Substitute.For<IFhirRequestContext>();
        requestContext.AccessControlContext.Returns(accessControlContext);

        var contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        contextAccessor.RequestContext.Returns(requestContext);

        searchService = Substitute.For<ISearchService>();

        return new GetResourceHandler(
            _fhirDataStore,
            _conformanceProvider,
            _resourceWrapperFactory,
            _resourceIdProvider,
            _dataResourceFilter,
            authService,
            contextAccessor,
            searchService);
    }

    private static SearchResult CreateSearchResult(params string[] resourceIds)
    {
        return new SearchResult(
            Array.ConvertAll(resourceIds, id => new SearchResultEntry(CreateWrapper(id))),
            continuationToken: null,
            sortOrder: null,
            unsupportedSearchParameters: Array.Empty<Tuple<string, string>>());
    }

    private static ResourceWrapper CreateWrapper(string id)
    {
        var patient = Samples.GetDefaultPatient().ToPoco<Patient>();
        patient.Id = id;

        return new ResourceWrapper(
            patient.ToResourceElement(),
            new RawResource(patient.ToJson(), FhirResourceFormat.Json, false),
            new ResourceRequest(System.Net.Http.HttpMethod.Get),
            false,
            null,
            null,
            null);
    }
}
