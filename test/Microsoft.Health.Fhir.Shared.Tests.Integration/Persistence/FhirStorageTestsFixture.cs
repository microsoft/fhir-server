// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Abstractions.Features.Transactions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Bundle;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Features.Resources.Create;
using Microsoft.Health.Fhir.Core.Features.Resources.Delete;
using Microsoft.Health.Fhir.Core.Features.Resources.Get;
using Microsoft.Health.Fhir.Core.Features.Resources.Upsert;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Filters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Common.Mocks;
using Microsoft.Health.SqlServer.Features.Schema;
using NSubstitute;
using Xunit;
using ResourceType = Hl7.Fhir.Model.ResourceType;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class FhirStorageTestsFixture : IAsyncLifetime, IDisposable
    {
        private readonly IServiceProvider _fixture;
        private readonly ResourceIdProvider _resourceIdProvider;
        private readonly DataResourceFilter _dataResourceFilter;

        public FhirStorageTestsFixture(DataStore dataStore)
            : this(dataStore switch
            {
                Common.FixtureParameters.DataStore.CosmosDb => new CosmosDbFhirStorageTestsFixture(),
                Common.FixtureParameters.DataStore.SqlServer => new SqlServerFhirStorageTestsFixture(),
                _ => throw new ArgumentOutOfRangeException(nameof(dataStore), dataStore, null),
            })
        {
        }

        internal FhirStorageTestsFixture(IServiceProvider fixture)
        {
            _fixture = fixture;

            // This step has to be done in the constructor because it uses an AsyncLocal and the tests run with the same
            // execution context as the fixture constructor, but not the same as InitializeAsync().
            _resourceIdProvider = new ResourceIdProvider();

            _dataResourceFilter = new DataResourceFilter(MissingDataFilterCriteria.Default);
        }

        public Mediator Mediator { get; private set; }

        public CapabilityStatement CapabilityStatement { get; private set; }

        public ConformanceProviderBase ConformanceProvider { get; private set; }

        public FhirJsonParser JsonParser { get; } = new FhirJsonParser();

        public ResourceDeserializer Deserializer { get; private set; }

        public IFhirDataStore DataStore => _fixture.GetRequiredService<IFhirDataStore>();

        public IFhirOperationDataStore OperationDataStore => _fixture.GetRequiredService<IFhirOperationDataStore>();

        public IFhirStorageTestHelper TestHelper => _fixture.GetRequiredService<IFhirStorageTestHelper>();

        public ISqlServerFhirStorageTestHelper SqlHelper => _fixture.GetRequiredService<ISqlServerFhirStorageTestHelper>();

        public ITransactionHandler TransactionHandler => _fixture.GetRequiredService<ITransactionHandler>();

        public ISearchParameterStatusDataStore SearchParameterStatusDataStore => _fixture.GetRequiredService<ISearchParameterStatusDataStore>();

        public FilebasedSearchParameterStatusDataStore FilebasedSearchParameterStatusDataStore => _fixture.GetRequiredService<FilebasedSearchParameterStatusDataStore>();

        public ISearchService SearchService => _fixture.GetRequiredService<ISearchService>();

        public SearchParameterDefinitionManager SearchParameterDefinitionManager => _fixture.GetRequiredService<SearchParameterDefinitionManager>();

        public SupportedSearchParameterDefinitionManager SupportedSearchParameterDefinitionManager => _fixture.GetRequiredService<SupportedSearchParameterDefinitionManager>();

        public SchemaInitializer SchemaInitializer => _fixture.GetRequiredService<SchemaInitializer>();

        public SchemaUpgradeRunner SchemaUpgradeRunner => _fixture.GetRequiredService<SchemaUpgradeRunner>();

        public SearchParameterStatusManager SearchParameterStatusManager => _fixture.GetRequiredService<SearchParameterStatusManager>();

        public RequestContextAccessor<IFhirRequestContext> FhirRequestContextAccessor => _fixture.GetRequiredService<RequestContextAccessor<IFhirRequestContext>>();

        public GetResourceHandler GetResourceHandler { get; set; }

        public void Dispose()
        {
            (_fixture as IDisposable)?.Dispose();
        }

        public async Task InitializeAsync()
        {
            if (_fixture is IAsyncLifetime asyncLifetime)
            {
                await asyncLifetime.InitializeAsync();
            }

            CapabilityStatement = CapabilityStatementMock.GetMockedCapabilityStatement();

            CapabilityStatementMock.SetupMockResource(CapabilityStatement, ResourceType.Observation, null);
            var observationResource = CapabilityStatement.Rest[0].Resource.Find(r => r.Type == ResourceType.Observation);
            observationResource.UpdateCreate = true;
            observationResource.Versioning = CapabilityStatement.ResourceVersionPolicy.Versioned;

            CapabilityStatementMock.SetupMockResource(CapabilityStatement, ResourceType.Organization, null);
            var organizationResource = CapabilityStatement.Rest[0].Resource.Find(r => r.Type == ResourceType.Organization);
            organizationResource.UpdateCreate = true;
            organizationResource.Versioning = CapabilityStatement.ResourceVersionPolicy.NoVersion;

            CapabilityStatementMock.SetupMockResource(CapabilityStatement, ResourceType.Medication, null);
            var medicationResource = CapabilityStatement.Rest[0].Resource.Find(r => r.Type == ResourceType.Medication);
            medicationResource.UpdateCreate = true;
            medicationResource.Versioning = CapabilityStatement.ResourceVersionPolicy.VersionedUpdate;

            ConformanceProvider = Substitute.For<ConformanceProviderBase>();
            ConformanceProvider.GetCapabilityStatementOnStartup().Returns(CapabilityStatement.ToTypedElement().ToResourceElement());

            // TODO: FhirRepository instantiate ResourceDeserializer class directly
            // which will try to deserialize the raw resource. We should mock it as well.
            var rawResourceFactory = Substitute.For<RawResourceFactory>(new FhirJsonSerializer());

            var resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
            resourceWrapperFactory
                .Create(Arg.Any<ResourceElement>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(x =>
                {
                    ResourceElement resource = x.ArgAt<ResourceElement>(0);
                    var searchParamHash = SearchParameterDefinitionManager.GetSearchParameterHashForResourceType(resource.InstanceType);

                    if (string.IsNullOrEmpty(searchParamHash))
                    {
                        searchParamHash = "hash";
                    }

                    return new ResourceWrapper(resource, rawResourceFactory.Create(resource, keepMeta: true), new ResourceRequest(HttpMethod.Post, "http://fhir"), x.ArgAt<bool>(1), null, null, null, searchParamHash);
                });

            UrlResolver urlResolver = CreateUrlResolver(FhirRequestContextAccessor);
            var bundleFactory = new BundleFactory(urlResolver, FhirRequestContextAccessor, NullLogger<BundleFactory>.Instance);

            GetResourceHandler = new GetResourceHandler(DataStore, new Lazy<IConformanceProvider>(() => ConformanceProvider), resourceWrapperFactory, _resourceIdProvider, _dataResourceFilter, DisabledFhirAuthorizationService.Instance, FhirRequestContextAccessor, SearchService);

            var collection = new ServiceCollection();

            collection.AddSingleton(typeof(IRequestHandler<CreateResourceRequest, UpsertResourceResponse>), new CreateResourceHandler(DataStore, new Lazy<IConformanceProvider>(() => ConformanceProvider), resourceWrapperFactory, _resourceIdProvider, new ResourceReferenceResolver(SearchService, new TestQueryStringParser()), DisabledFhirAuthorizationService.Instance));
            collection.AddSingleton(typeof(IRequestHandler<UpsertResourceRequest, UpsertResourceResponse>), new UpsertResourceHandler(DataStore, new Lazy<IConformanceProvider>(() => ConformanceProvider), resourceWrapperFactory, _resourceIdProvider, DisabledFhirAuthorizationService.Instance, ModelInfoProvider.Instance));
            collection.AddSingleton(typeof(IRequestHandler<GetResourceRequest, GetResourceResponse>), GetResourceHandler);
            collection.AddSingleton(typeof(IRequestHandler<DeleteResourceRequest, DeleteResourceResponse>), new DeleteResourceHandler(DataStore, new Lazy<IConformanceProvider>(() => ConformanceProvider), resourceWrapperFactory, _resourceIdProvider, DisabledFhirAuthorizationService.Instance));
            collection.AddSingleton(typeof(IRequestHandler<SearchResourceHistoryRequest, SearchResourceHistoryResponse>), new SearchResourceHistoryHandler(SearchService, bundleFactory, DisabledFhirAuthorizationService.Instance, new DataResourceFilter(MissingDataFilterCriteria.Default)));
            collection.AddSingleton(typeof(IRequestHandler<SearchResourceRequest, SearchResourceResponse>), new SearchResourceHandler(SearchService, bundleFactory, DisabledFhirAuthorizationService.Instance, new DataResourceFilter(MissingDataFilterCriteria.Default)));

            ServiceProvider services = collection.BuildServiceProvider();

            Mediator = new Mediator(type => services.GetService(type));

            Deserializer = new ResourceDeserializer(
                (FhirResourceFormat.Json, new Func<string, string, DateTimeOffset, ResourceElement>((str, version, lastUpdated) => JsonParser.Parse(str).ToResourceElement())));
        }

        public async Task DisposeAsync()
        {
            if (_fixture is IAsyncLifetime asyncLifetime)
            {
                await asyncLifetime.DisposeAsync();
            }
        }

        private static UrlResolver CreateUrlResolver(RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor)
        {
            IUrlHelperFactory urlHelperFactory = Substitute.For<IUrlHelperFactory>();
            IHttpContextAccessor httpContextAccessor = Substitute.For<IHttpContextAccessor>();
            IActionContextAccessor actionContextAccessor = Substitute.For<IActionContextAccessor>();
            IBundleHttpContextAccessor bundleHttpContextAccessor = Substitute.For<IBundleHttpContextAccessor>();
            IUrlHelper urlHelper = Substitute.For<IUrlHelper>();

            var httpContext = new DefaultHttpContext();
            var actionContext = new ActionContext();

            const string scheme = "scheme";
            const string host = "test";

            httpContextAccessor.HttpContext.Returns(httpContext);

            httpContext.Request.Scheme = scheme;
            httpContext.Request.Host = new HostString(host);

            actionContextAccessor.ActionContext.Returns(actionContext);

            urlHelper.RouteUrl(Arg.Do<UrlRouteContext>(_ => { }));
            urlHelperFactory.GetUrlHelper(actionContext).Returns(urlHelper);
            urlHelper.RouteUrl(Arg.Any<UrlRouteContext>()).Returns($"{scheme}://{host}");

            bundleHttpContextAccessor.HttpContext.Returns((HttpContext)null);

            return new UrlResolver(
                fhirRequestContextAccessor,
                urlHelperFactory,
                httpContextAccessor,
                actionContextAccessor,
                bundleHttpContextAccessor);
        }
    }
}
