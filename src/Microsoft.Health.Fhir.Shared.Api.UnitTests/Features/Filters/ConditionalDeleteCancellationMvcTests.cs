// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Medino;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Delete;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class ConditionalDeleteCancellationMvcTests
    {
        private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
        private readonly ILogger<DeletionService> _deleteLogger = Substitute.For<ILogger<DeletionService>>();
        private readonly ILogger<OperationOutcomeExceptionFilterAttribute> _filterLogger = Substitute.For<ILogger<OperationOutcomeExceptionFilterAttribute>>();
        private readonly string _correlationId = Guid.NewGuid().ToString();

        [Theory]
        [InlineData(0, HttpStatusCode.RequestTimeout)]
        [InlineData(1, HttpStatusCode.InternalServerError)]
        public async Task GivenPartiallyCompletedHardDelete_WhenMvcHandlesTheFailure_ThenStatusCountsAndAuditingArePreserved(int failureKind, HttpStatusCode expectedStatus)
        {
            // Arrange
            Exception failure = failureKind switch
            {
                0 => new TaskCanceledException(),
                1 => new AggregateException(new TaskCanceledException(), new InvalidOperationException("Storage failure.")),
                _ => throw new ArgumentOutOfRangeException(nameof(failureKind)),
            };
            using var host = CreateHost(failure);
            using var client = host.GetTestClient();
            client.DefaultRequestHeaders.Accept.ParseAdd("application/fhir+json");

            // Act
            using var response = await client.DeleteAsync("/delete-cancellation/Patient");

            // Assert
            Assert.Equal(expectedStatus, response.StatusCode);
            Assert.Equal("1", Assert.Single(response.Headers.GetValues(KnownHeaders.ItemsDeleted)));
            var outcome = new FhirJsonParser().Parse<OperationOutcome>(await response.Content.ReadAsStringAsync());
            Assert.Equal(_correlationId, outcome.Id);
            var issue = Assert.Single(outcome.Issue);
            Assert.Equal(OperationOutcome.IssueSeverity.Error, issue.Severity);
            Assert.Equal(expectedStatus == HttpStatusCode.RequestTimeout ? OperationOutcome.IssueType.Timeout : OperationOutcome.IssueType.Unknown, issue.Code);
            Assert.Equal(expectedStatus == HttpStatusCode.RequestTimeout ? Core.Resources.OperationCanceled : null, issue.Diagnostics);
            var completedAudit = Assert.Single(_auditLogger.ReceivedCalls(), call =>
                call.GetMethodInfo().Name == nameof(IAuditLogger.LogAudit) && (AuditAction)call.GetArguments()[0] == AuditAction.Executed);
            var properties = Assert.Single(completedAudit.GetArguments().OfType<IReadOnlyDictionary<string, string>>(), value => value.ContainsKey("Affected Items"));
            Assert.Equal("[Include] Patient/completed", properties["Affected Items"]);
            Assert.Contains(_deleteLogger.ReceivedCalls(), call =>
                call.GetMethodInfo().Name == nameof(ILogger.Log) && (LogLevel)call.GetArguments()[0] == LogLevel.Error && call.GetArguments()[3] is Exception);
            var filterLog = Assert.Single(_filterLogger.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(ILogger.Log));
            Assert.Equal(expectedStatus == HttpStatusCode.RequestTimeout ? LogLevel.Warning : LogLevel.Error, filterLog.GetArguments()[0]);
        }

        private IHost CreateHost(Exception failure)
        {
            var contextAccessor = new FhirRequestContextAccessor();
            var dataStore = Substitute.For<IFhirDataStore>();
            dataStore.HardDeleteAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<ResourceKey>().Id == "completed" ? Task.CompletedTask : Task.FromException(failure));
            var dataStoreFactory = Substitute.For<IDeletionServiceDataStoreFactory>();
            dataStoreFactory.GetScopedDataStore().Returns(new DeletionServiceScopedDataStore(dataStore));
            var searchService = Substitute.For<ISearchService>();
            searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()).Returns(Task.FromResult(new SearchResult(
                    new[]
                    {
                        CreateEntry("completed", SearchEntryMode.Include),
                        CreateEntry("failed", SearchEntryMode.Match),
                    },
                    null,
                    null,
                    Array.Empty<Tuple<string, string>>())));
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            var searchFactory = Substitute.For<IScopeProvider<ISearchService>>();
            searchFactory.Invoke().Returns(scopedSearchService);
            var authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<DataActions>());
            var conformanceProvider = new Lazy<IConformanceProvider>(() => Substitute.For<IConformanceProvider>());
            var wrapperFactory = Substitute.For<IResourceWrapperFactory>();
            var runtimeConfiguration = Substitute.For<IFhirRuntimeConfiguration>();
            runtimeConfiguration.DataStore.Returns(KnownDataStores.SqlServer);
            var configuration = Options.Create(new CoreFeatureConfiguration { SupportsIncludes = true });

            return new HostBuilder().ConfigureWebHost(builder => builder.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<RequestContextAccessor<IFhirRequestContext>>(contextAccessor);
                    services.AddSingleton(_filterLogger);
                    services.AddSingleton(provider => new DeletionService(
                        wrapperFactory,
                        conformanceProvider,
                        dataStoreFactory,
                        searchFactory,
                        new ResourceIdProvider(),
                        contextAccessor,
                        _auditLogger,
                        configuration,
                        runtimeConfiguration,
                        Substitute.For<ISearchParameterOperations>(),
                        Substitute.For<IResourceDeserializer>(),
                        Substitute.For<IProvideProfilesForValidation>(),
                        authorizationService,
                        _deleteLogger));
                    services.AddSingleton(provider => new ConditionalDeleteResourceHandler(
                        dataStore,
                        conformanceProvider,
                        wrapperFactory,
                        searchService,
                        Substitute.For<IMediator>(),
                        new ResourceIdProvider(),
                        authorizationService,
                        provider.GetRequiredService<DeletionService>(),
                        contextAccessor,
                        configuration,
                        Substitute.For<ILogger<ConditionalDeleteResourceHandler>>()));
                    services.AddControllers(options =>
                    {
                        options.Filters.Add<OperationOutcomeExceptionFilterAttribute>();
                        options.OutputFormatters.Clear();
                        options.OutputFormatters.Add(new FhirJsonOutputFormatter(
                            new FhirJsonSerializer(),
                            Deserializers.ResourceDeserializer,
                            ArrayPool<char>.Shared,
                            new BundleSerializer(),
                            ModelInfoProvider.Instance));
                    }).ConfigureApplicationPartManager(manager =>
                    {
                        manager.ApplicationParts.Clear();
                        manager.FeatureProviders.Add(new DeleteControllerFeatureProvider());
                    });
                })
                .Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        contextAccessor.RequestContext = new FhirRequestContext(
                            "DELETE",
                            "https://localhost/delete-cancellation/Patient",
                            "https://localhost/",
                            _correlationId,
                            new Dictionary<string, StringValues>(),
                            context.Response.Headers);
                        try
                        {
                            await next();
                        }
                        finally
                        {
                            contextAccessor.RequestContext = null;
                        }
                    });
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                })).Start();
        }

        private static SearchResultEntry CreateEntry(string id, SearchEntryMode mode)
        {
            var patient = new Patient { Id = id };
            var wrapper = new ResourceWrapper(
                patient.ToResourceElement(),
                new RawResource(patient.ToJson(), FhirResourceFormat.Json, false),
                null,
                false,
                null,
                null,
                null);
            return new SearchResultEntry(wrapper, mode);
        }

        public sealed class DeleteCancellationController : ControllerBase
        {
            private readonly ConditionalDeleteResourceHandler _handler;

            public DeleteCancellationController(ConditionalDeleteResourceHandler handler)
            {
                _handler = handler;
            }

            [HttpDelete("/delete-cancellation/Patient")]
            public async Task<IActionResult> Delete(CancellationToken cancellationToken)
            {
                await _handler.HandleAsync(
                    new ConditionalDeleteResourceRequest(
                        "Patient",
                        new[] { Tuple.Create("active", "false") },
                        DeleteOperation.HardDelete,
                        maxDeleteCount: 10),
                    cancellationToken);
                return NoContent();
            }
        }

        private sealed class DeleteControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
        {
            public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
            {
                feature.Controllers.Add(typeof(DeleteCancellationController).GetTypeInfo());
            }
        }
    }
}
