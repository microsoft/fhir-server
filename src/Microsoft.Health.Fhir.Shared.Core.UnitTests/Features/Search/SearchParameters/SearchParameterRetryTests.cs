// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchParameters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterRetryTests
    {
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly IFhirDataStore _fhirDataStore;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterStatusManager _searchParameterStatusManager;
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly IFhirRequestContext _fhirRequestContext;
        private readonly IModelInfoProvider _modelInfoProvider;

        public SearchParameterRetryTests()
        {
            _searchParameterOperations = Substitute.For<ISearchParameterOperations>();
            _fhirDataStore = Substitute.For<IFhirDataStore>();
            _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            _searchParameterStatusManager = Substitute.For<ISearchParameterStatusManager>();
            _modelInfoProvider = ModelInfoProvider.Instance;

            _fhirRequestContext = Substitute.For<IFhirRequestContext>();
            _fhirRequestContext.Properties.Returns(new Dictionary<string, object>());

            _requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            _requestContextAccessor.RequestContext.Returns(_fhirRequestContext);

            _searchParameterOperations.SearchParamLastUpdated.Returns(DateTimeOffset.UtcNow);
        }

        [Fact]
        public async Task CreateBehavior_GivenConcurrencyConflict_RetriesAndSucceeds()
        {
            var searchParameter = new SearchParameter { Id = "test", Url = "http://test.com/param" };
            var resource = searchParameter.ToTypedElement().ToResourceElement();
            var request = new CreateResourceRequest(resource, bundleResourceContext: null);

            var attemptCount = 0;
            _searchParameterOperations
                .ValidateSearchParameterAsync(Arg.Any<ITypedElement>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
                .Returns(callInfo =>
                {
                    attemptCount++;
                    if (attemptCount < 3)
                    {
                        throw new BadRequestException(Core.Resources.SearchParameterConcurrencyConflict);
                    }

                    return DateTimeOffset.UtcNow;
                });

            var wrapper = CreateMockResourceWrapper(resource);
            var response = new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(wrapper), SaveOutcomeType.Created));

            var behavior = new CreateOrUpdateSearchParameterBehavior<CreateResourceRequest, UpsertResourceResponse>(
                _searchParameterOperations,
                _fhirDataStore,
                _searchParameterDefinitionManager,
                _requestContextAccessor,
                _modelInfoProvider);

            var result = await behavior.Handle(request, ct => Task.FromResult(response), CancellationToken.None);

            Assert.Equal(3, attemptCount);
            Assert.NotNull(result);
        }

        [Fact]
        public async Task CreateBehavior_GivenConcurrencyConflictExhaustsRetries_ThrowsWithRetryCount()
        {
            var searchParameter = new SearchParameter { Id = "test", Url = "http://test.com/param" };
            var resource = searchParameter.ToTypedElement().ToResourceElement();
            var request = new CreateResourceRequest(resource, bundleResourceContext: null);

            _searchParameterOperations
                .ValidateSearchParameterAsync(Arg.Any<ITypedElement>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
                .Returns(Task.FromException<DateTimeOffset>(new BadRequestException(Core.Resources.SearchParameterConcurrencyConflict)));

            var behavior = new CreateOrUpdateSearchParameterBehavior<CreateResourceRequest, UpsertResourceResponse>(
                _searchParameterOperations,
                _fhirDataStore,
                _searchParameterDefinitionManager,
                _requestContextAccessor,
                _modelInfoProvider);

            var exception = await Assert.ThrowsAsync<BadRequestException>(async () =>
                await behavior.Handle(request, ct => Task.FromResult<UpsertResourceResponse>(null), CancellationToken.None));

            Assert.Contains("Retries=3", exception.Message);
        }

        [Fact]
        public async Task CreateBehavior_GivenLastUpdatedInContext_DoesNotRetry()
        {
            var lastUpdated = DateTimeOffset.UtcNow;
            var properties = new Dictionary<string, object>
            {
                [SearchParameterRequestContextPropertyNames.LastUpdated] = lastUpdated,
            };
            _fhirRequestContext.Properties.Returns(properties);

            var searchParameter = new SearchParameter { Id = "test", Url = "http://test.com/param" };
            var resource = searchParameter.ToTypedElement().ToResourceElement();
            var request = new CreateResourceRequest(resource, bundleResourceContext: null);

            var attemptCount = 0;
            _searchParameterOperations
                .ValidateSearchParameterAsync(Arg.Any<ITypedElement>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
                .ReturnsForAnyArgs(_ =>
                {
                    attemptCount++;
                    return Task.FromException<DateTimeOffset>(new BadRequestException(Core.Resources.SearchParameterConcurrencyConflict));
                });

            var behavior = new CreateOrUpdateSearchParameterBehavior<CreateResourceRequest, UpsertResourceResponse>(
                _searchParameterOperations,
                _fhirDataStore,
                _searchParameterDefinitionManager,
                _requestContextAccessor,
                _modelInfoProvider);

            var exception = await Assert.ThrowsAsync<BadRequestException>(async () =>
                await behavior.Handle(request, ct => Task.FromResult<UpsertResourceResponse>(null), CancellationToken.None));

            Assert.Equal(1, attemptCount);
            Assert.DoesNotContain("Retries=", exception.Message);
        }

        [Fact]
        public async Task DeleteBehavior_GivenConcurrencyConflict_RetriesAndSucceeds()
        {
            var searchParameter = new SearchParameter { Id = "test", Url = "http://test.com/param" };
            var resource = searchParameter.ToTypedElement().ToResourceElement();
            var wrapper = CreateMockResourceWrapper(resource);

            var key = new ResourceKey("SearchParameter", "test");
            var request = new DeleteResourceRequest(key, DeleteOperation.SoftDelete);

            var attemptCount = 0;
            _fhirDataStore.GetAsync(key, Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    attemptCount++;
                    if (attemptCount < 2)
                    {
                        throw new BadRequestException(Core.Resources.SearchParameterConcurrencyConflict);
                    }

                    return Task.FromResult(wrapper);
                });

            var response = new DeleteResourceResponse(key);
            var behavior = new DeleteSearchParameterBehavior<DeleteResourceRequest, DeleteResourceResponse>(
                _searchParameterOperations,
                _fhirDataStore,
                _searchParameterDefinitionManager,
                _searchParameterStatusManager,
                _requestContextAccessor,
                _modelInfoProvider);

            var result = await behavior.Handle(request, ct => Task.FromResult(response), CancellationToken.None);

            Assert.Equal(2, attemptCount);
            Assert.NotNull(result);
        }

        [Fact]
        public async Task DeleteBehavior_GivenLastUpdatedInContext_DoesNotRetry()
        {
            var lastUpdated = DateTimeOffset.UtcNow;
            var properties = new Dictionary<string, object>
            {
                [SearchParameterRequestContextPropertyNames.LastUpdated] = lastUpdated,
            };
            _fhirRequestContext.Properties.Returns(properties);

            var key = new ResourceKey("SearchParameter", "test");
            var request = new DeleteResourceRequest(key, DeleteOperation.SoftDelete);

            var attemptCount = 0;
            _fhirDataStore.GetAsync(key, Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs(_ =>
                {
                    attemptCount++;
                    return Task.FromException<ResourceWrapper>(new BadRequestException(Core.Resources.SearchParameterConcurrencyConflict));
                });

            var behavior = new DeleteSearchParameterBehavior<DeleteResourceRequest, DeleteResourceResponse>(
                _searchParameterOperations,
                _fhirDataStore,
                _searchParameterDefinitionManager,
                _searchParameterStatusManager,
                _requestContextAccessor,
                _modelInfoProvider);

            var exception = await Assert.ThrowsAsync<BadRequestException>(async () =>
                await behavior.Handle(request, ct => Task.FromResult<DeleteResourceResponse>(null), CancellationToken.None));

            Assert.Equal(1, attemptCount);
            Assert.DoesNotContain("Retries=", exception.Message);
        }

        private ResourceWrapper CreateMockResourceWrapper(ResourceElement resource)
        {
            var rawJson = new FhirJsonSerializer().SerializeToString(resource.ToPoco<Resource>());
            return new ResourceWrapper(
                resource,
                new RawResource(rawJson, FhirResourceFormat.Json, isMetaSet: false),
                new ResourceRequest(HttpMethod.Post, "http://fhir"),
                false,
                null,
                null,
                null,
                null);
        }
    }
}
