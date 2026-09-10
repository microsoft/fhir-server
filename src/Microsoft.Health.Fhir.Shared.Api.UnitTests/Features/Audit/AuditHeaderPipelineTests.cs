// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Medino;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Configs;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ApiNotifications;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Claim = System.Security.Claims.Claim;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Audit
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Audit)]
    public class AuditHeaderPipelineTests : IAsyncLifetime
    {
        private const string HeaderName = "X-MS-AZUREFHIR-AUDIT-test";
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
        private readonly FhirRequestContextAccessor _accessor = new FhirRequestContextAccessor();
        private IHost _host;
        private HttpClient _client;

        public async Task InitializeAsync()
        {
            _mediator.SendAsync<SearchResourceResponse>(Arg.Any<SearchResourceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new SearchResourceResponse(new Hl7.Fhir.Model.Bundle { Type = Hl7.Fhir.Model.Bundle.BundleType.Searchset }.ToResourceElement()));

            _host = new HostBuilder().ConfigureWebHost(webHost => webHost
                .UseKestrel()
                .UseUrls("http://127.0.0.1:0")
                .ConfigureServices(services =>
                {
                    services.AddLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Warning));
                    services.AddAuthorization();
                    var authentication = Substitute.For<IAuthenticationService>();
                    authentication.ChallengeAsync(Arg.Any<Microsoft.AspNetCore.Http.HttpContext>(), Arg.Any<string>(), Arg.Any<AuthenticationProperties>())
                        .Returns(call =>
                        {
                            call.Arg<Microsoft.AspNetCore.Http.HttpContext>().Response.StatusCode = 401;
                            return Task.CompletedTask;
                        });
                    authentication.ForbidAsync(Arg.Any<Microsoft.AspNetCore.Http.HttpContext>(), Arg.Any<string>(), Arg.Any<AuthenticationProperties>())
                        .Returns(call =>
                        {
                            call.Arg<Microsoft.AspNetCore.Http.HttpContext>().Response.StatusCode = 403;
                            return Task.CompletedTask;
                        });
                    services.AddSingleton(authentication);
                    services.AddRouting();
                    services.AddControllers(options =>
                    {
                        options.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder()
                            .RequireAuthenticatedUser().RequireClaim("test-access").Build()));
                        options.OutputFormatters.Insert(0, new FhirJsonOutputFormatter(
                            new FhirJsonSerializer(),
                            Deserializers.ResourceDeserializer,
                            ArrayPool<char>.Shared,
                            new BundleSerializer(),
                            ModelInfoProvider.Instance));
                        options.OutputFormatters.Insert(1, new FhirXmlOutputFormatter(
                            new FhirXmlSerializer(), Deserializers.ResourceDeserializer, ModelInfoProvider.Instance));
                    }).AddApplicationPart(typeof(FhirController).Assembly).AddControllersAsServices();

                    new MvcModule().Load(services);
                    services.AddSingleton<RequestContextAccessor<IFhirRequestContext>>(_accessor);
                    services.AddSingleton(_mediator);
                    services.AddSingleton(_auditLogger);
                    services.AddSingleton<IClaimsExtractor>(Substitute.For<IClaimsExtractor>());
                    services.AddSingleton<IAuditHeaderReader, AuditHeaderReader>();
                    services.AddSingleton(Options.Create(new AuditConfiguration { CustomAuditHeaderPrefix = "X-MS-AZUREFHIR-AUDIT-" }));
                    services.AddSingleton<IAuditHelper, AuditHelper>();
                    services.AddSingleton<AuditEventTypeMapping>();
                    services.AddSingleton<IAuditEventTypeMapping>(provider => provider.GetRequiredService<AuditEventTypeMapping>());
                    services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<AuditEventTypeMapping>());
                    services.AddTransient<ApiNotificationMiddleware>();
                    services.AddSingleton<AuditLoggingFilterAttribute>();
                    services.AddSingleton<OperationOutcomeExceptionFilterAttribute>();
                    services.AddSingleton<ValidateExportRequestFilterAttribute>();
                    services.AddSingleton(new ValidateFormatParametersAttribute(Substitute.For<IFormatParametersValidator>()));
                    services.AddSingleton(new QueryLatencyOverEfficiencyFilterAttribute(_accessor, Substitute.For<IFhirRuntimeConfiguration>()));
                    services.AddSingleton(new QueryCacheFilterAttribute(_accessor, Substitute.For<IFhirRuntimeConfiguration>()));
                    services.AddSingleton(Substitute.For<ISearchMetricHandler>());
                    services.AddSingleton(Substitute.For<IBundleMetricHandler>());
                    services.AddTransient(_ => Mock.TypeWithArguments<FhirController>(_mediator, _accessor, Options.Create(new FeatureConfiguration())));
                })
                .Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        context.User = new ClaimsPrincipal(context.Request.Headers.ContainsKey("X-Test-Anonymous")
                            ? new ClaimsIdentity()
                            : new ClaimsIdentity(
                                context.Request.Headers.ContainsKey("X-Test-Forbidden") ? Array.Empty<Claim>() : new[] { new Claim("test-access", "true") },
                                "test"));
                        _accessor.RequestContext = new DefaultFhirRequestContext
                        {
                            CorrelationId = "audit-header-test",
                            Uri = new Uri($"http://localhost{context.Request.Path}"),
                            RequestHeaders = context.Request.Headers,
                            Principal = context.User,
                        };
                        await next();
                    });
                    app.UseApiNotifications();
                    app.UseExceptionHandler("/CustomError");
                    app.UseStatusCodePagesWithReExecute("/CustomError", "?statusCode={0}");
                    app.UseMiddleware<AuditMiddleware>();
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }))
                .Build();

            await _host.StartAsync();
            string address = _host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single();
            _client = new HttpClient { BaseAddress = new Uri(address) };
        }

        [Theory]
        [InlineData(2049, "application/fhir+json", "/Patient")]
        [InlineData(3763, "application/fhir+json", "/Patient")]
        [InlineData(2049, "application/fhir+xml", "/Patient")]
        [InlineData(2049, "application/fhir+json", "/$export")]
        public async Task GivenAnOversizedAuditHeader_WhenRequestingAFhirEndpoint_ThenReturn431OperationOutcome(int length, string mediaType, string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add(HeaderName, new string('a', length));
            request.Headers.Accept.ParseAdd(mediaType);

            using HttpResponseMessage response = await _client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(431, (int)response.StatusCode);
            Assert.Equal(mediaType, response.Content.Headers.ContentType.MediaType);
            OperationOutcome outcome = mediaType.EndsWith("json", StringComparison.Ordinal)
                ? new FhirJsonParser().Parse<OperationOutcome>(body)
                : new FhirXmlParser().Parse<OperationOutcome>(body);
            Assert.Equal("audit-header-test", outcome.Id);
            Assert.Equal(OperationOutcome.IssueType.Invalid, Assert.Single(outcome.Issue).Code);
            Assert.Empty(_auditLogger.ReceivedCalls());
            await _mediator.DidNotReceive().SendAsync<SearchResourceResponse>(Arg.Any<SearchResourceRequest>(), Arg.Any<CancellationToken>());
            AssertResponseNotification(
                HttpStatusCode.RequestHeaderFieldsTooLarge,
                path == "/$export" ? AuditEventSubType.Export : AuditEventSubType.SearchType,
                path == "/$export" ? null : "Patient");
        }

        [Fact]
        public async Task GivenTooManyAuditHeaders_WhenSearching_ThenReturn431OperationOutcome()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/Patient");
            for (int i = 0; i <= AuditConstants.MaximumNumberOfCustomHeaders; i++)
            {
                request.Headers.Add(HeaderName + i, "value");
            }

            using HttpResponseMessage response = await _client.SendAsync(request);
            var outcome = new FhirJsonParser().Parse<OperationOutcome>(await response.Content.ReadAsStringAsync());

            Assert.Equal(431, (int)response.StatusCode);
            Assert.Equal(OperationOutcome.IssueType.Invalid, Assert.Single(outcome.Issue).Code);
            Assert.Empty(_auditLogger.ReceivedCalls());
            await _mediator.DidNotReceive().SendAsync<SearchResourceResponse>(Arg.Any<SearchResourceRequest>(), Arg.Any<CancellationToken>());
            AssertResponseNotification(HttpStatusCode.RequestHeaderFieldsTooLarge, AuditEventSubType.SearchType, "Patient");
        }

        [Theory]
        [InlineData("X-Test-Anonymous", 401)]
        [InlineData("X-Test-Forbidden", 403)]
        public async Task GivenAnOversizedHeader_WhenAuthorizationFails_ThenPreserveAuthorizationError(string header, int expectedStatus)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/Patient");
            request.Headers.Add(header, "true");
            request.Headers.Add(HeaderName, new string('a', 2049));

            using HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(expectedStatus, (int)response.StatusCode);
            var outcome = new FhirJsonParser().Parse<OperationOutcome>(await response.Content.ReadAsStringAsync());
            Assert.NotEqual(OperationOutcome.IssueType.Invalid, Assert.Single(outcome.Issue).Code);
            await _mediator.DidNotReceive().SendAsync<SearchResourceResponse>(Arg.Any<SearchResourceRequest>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData("batch")]
        [InlineData("transaction")]
        [InlineData("invalid")]
        public async Task GivenAnOversizedHeader_WhenPostingABundle_ThenRejectBeforeProcessing(string bundleType)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/");
            request.Headers.Add(HeaderName, new string('a', 2049));
            request.Content = new StringContent(
                $"{{\"resourceType\":\"Bundle\",\"type\":\"{bundleType}\",\"entry\":[{{\"request\":{{\"method\":\"GET\",\"url\":\"Patient\"}}}}]}}",
                System.Text.Encoding.UTF8,
                "application/fhir+json");

            using HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(431, (int)response.StatusCode);
            var outcome = new FhirJsonParser().Parse<OperationOutcome>(await response.Content.ReadAsStringAsync());
            Assert.Equal(OperationOutcome.IssueType.Invalid, Assert.Single(outcome.Issue).Code);
            await _mediator.DidNotReceive().SendAsync<BundleResponse>(Arg.Any<BundleRequest>(), Arg.Any<CancellationToken>());
            Assert.Empty(_auditLogger.ReceivedCalls());
            AssertResponseNotification(HttpStatusCode.RequestHeaderFieldsTooLarge, AuditEventSubType.BundlePost, null);
        }

        [Theory]
        [InlineData(0, 0, "application/fhir+json")]
        [InlineData(1, 2048, "application/fhir+json")]
        [InlineData(AuditConstants.MaximumNumberOfCustomHeaders, 1, "application/fhir+json")]
        [InlineData(0, 0, "application/fhir+xml")]
        [InlineData(1, 2048, "application/fhir+xml")]
        [InlineData(AuditConstants.MaximumNumberOfCustomHeaders, 1, "application/fhir+xml")]
        public async Task GivenValidAuditHeaders_WhenSearching_ThenSearchAndAuditSucceed(int count, int length, string mediaType)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/Patient");
            for (int i = 0; i < count; i++)
            {
                request.Headers.Add(HeaderName + i, new string('a', length));
            }

            request.Headers.Accept.ParseAdd(mediaType);

            using HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(mediaType, response.Content.Headers.ContentType.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            Hl7.Fhir.Model.Bundle bundle = mediaType.EndsWith("json", StringComparison.Ordinal)
                ? new FhirJsonParser().Parse<Hl7.Fhir.Model.Bundle>(body)
                : new FhirXmlParser().Parse<Hl7.Fhir.Model.Bundle>(body);
            Assert.Equal(Hl7.Fhir.Model.Bundle.BundleType.Searchset, bundle.Type);
            await _mediator.Received(1).SendAsync<SearchResourceResponse>(Arg.Any<SearchResourceRequest>(), Arg.Any<CancellationToken>());
            Assert.Equal(2, _auditLogger.ReceivedCalls().Count());
            AssertResponseNotification(HttpStatusCode.OK, AuditEventSubType.SearchType, "Patient");
        }

        public async Task DisposeAsync()
        {
            _client?.Dispose();
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }

        private void AssertResponseNotification(HttpStatusCode status, string operation, string resourceType)
        {
            var notification = Assert.Single(_mediator.ReceivedCalls()
                .SelectMany(call => call.GetArguments())
                .OfType<ApiResponseNotification>());
            Assert.Equal(status, notification.StatusCode);
            Assert.Equal(operation, notification.FhirOperation);
            Assert.Equal(resourceType, notification.ResourceType);
            Assert.Equal("test", notification.Authentication);
            Assert.Equal("http", notification.Protocol);
            Assert.True(notification.Latency >= TimeSpan.Zero);
        }
    }
}
