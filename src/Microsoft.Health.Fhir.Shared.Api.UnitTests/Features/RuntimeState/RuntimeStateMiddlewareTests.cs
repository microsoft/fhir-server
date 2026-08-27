// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Api.Features.Logging;
using Microsoft.Health.Fhir.Api.Features.RuntimeState;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Features.RuntimeState
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.ServiceRuntimeState)]
    public class RuntimeStateMiddlewareTests
    {
        public static IEnumerable<object[]> AllowedDeprecatedRequests()
        {
            yield return new object[] { HttpMethods.Get, "/health/check" };
            yield return new object[] { HttpMethods.Get, "/metadata" };
            yield return new object[] { HttpMethods.Get, "/metadata/" };
            yield return new object[] { HttpMethods.Get, "/$export" };
            yield return new object[] { HttpMethods.Get, "/Patient/$export" };
            yield return new object[] { HttpMethods.Get, "/Group/group-id/$export" };
            yield return new object[] { HttpMethods.Get, "/_operations/export/job-id" };
        }

        public static IEnumerable<object[]> BlockedDeprecatedRequests()
        {
            yield return new object[] { HttpMethods.Post, "/Patient" };
            yield return new object[] { HttpMethods.Put, "/Patient/patient-id" };
            yield return new object[] { HttpMethods.Patch, "/Patient/patient-id" };
            yield return new object[] { HttpMethods.Delete, "/Patient/patient-id" };
            yield return new object[] { HttpMethods.Get, "/Patient?name=Smith" };
            yield return new object[] { HttpMethods.Get, "/Patient/_history" };
            yield return new object[] { HttpMethods.Post, "/" };
            yield return new object[] { HttpMethods.Post, "/$import" };
            yield return new object[] { HttpMethods.Delete, "/$bulk-delete" };
            yield return new object[] { HttpMethods.Post, "/$reindex" };
            yield return new object[] { HttpMethods.Post, "/$convert-data" };
            yield return new object[] { HttpMethods.Delete, "/_operations/export/job-id" };
            yield return new object[] { HttpMethods.Get, "/Group//group-id/$export" };
            yield return new object[] { HttpMethods.Get, "/patient/$export" };
            yield return new object[] { HttpMethods.Get, "//$export" };
        }

        [Theory]
        [MemberData(nameof(AllowedDeprecatedRequests))]
        public async Task GivenDeprecatedService_WhenRequestIsAllowed_ThenRequestContinues(
            string method,
            string path)
        {
            bool nextInvoked = false;
            RuntimeStateMiddleware middleware = CreateMiddleware(
                FhirRuntimeState.Deprecated,
                _ =>
                {
                    nextInvoked = true;
                    return Task.CompletedTask;
                });
            DefaultHttpContext context = CreateContext(method, path);

            await middleware.Invoke(context);

            Assert.True(nextInvoked);
        }

        [Theory]
        [MemberData(nameof(BlockedDeprecatedRequests))]
        public async Task GivenDeprecatedService_WhenRequestIsBlocked_ThenGoneOperationOutcomeIsReturned(
            string method,
            string path)
        {
            RuntimeStateMiddleware middleware = CreateMiddleware(
                FhirRuntimeState.Deprecated,
                _ => throw new Xunit.Sdk.XunitException("The blocked request reached the next middleware."));
            DefaultHttpContext context = CreateContext(method, path);
            var stopwatch = Stopwatch.StartNew();

            await middleware.Invoke(context);

            stopwatch.Stop();
            Assert.Equal(StatusCodes.Status410Gone, context.Response.StatusCode);
            Assert.Equal(KnownContentTypes.JsonContentType, context.Response.ContentType);
            Assert.True(stopwatch.ElapsedMilliseconds >= 45);

            context.Response.Body.Position = 0;

            using JsonDocument outcome = await JsonDocument.ParseAsync(context.Response.Body);

            JsonElement issue = outcome.RootElement.GetProperty("issue")[0];

            Assert.Equal("OperationOutcome", outcome.RootElement.GetProperty("resourceType").GetString());
            Assert.Equal("error", issue.GetProperty("severity").GetString());
            Assert.Equal("business-rule", issue.GetProperty("code").GetString());
            Assert.Equal("service-deprecated", issue.GetProperty("details").GetProperty("coding")[0].GetProperty("code").GetString());
            Assert.Contains("This FHIR service is deprecated no longer accepts normal workloads.", issue.GetProperty("diagnostics").GetString());

            Assert.DoesNotContain("patient-id", issue.GetRawText());
            Assert.DoesNotContain("Smith", issue.GetRawText());
        }

        [Fact]
        public async Task GivenActiveService_WhenRequestWouldBeBlockedForDeprecatedService_ThenRequestContinues()
        {
            bool nextInvoked = false;
            RuntimeStateMiddleware middleware = CreateMiddleware(
                FhirRuntimeState.Active,
                _ =>
                {
                    nextInvoked = true;
                    return Task.CompletedTask;
                });
            DefaultHttpContext context = CreateContext(HttpMethods.Post, "/Patient");

            await middleware.Invoke(context);

            Assert.True(nextInvoked);
        }

        private static RuntimeStateMiddleware CreateMiddleware(
            FhirRuntimeState runtimeState,
            RequestDelegate next)
        {
            IFhirRuntimeConfiguration configuration = Substitute.For<IFhirRuntimeConfiguration>();
            configuration.RuntimeState.Returns(runtimeState);

            IHttpInboundRequestLogger inboundRequestLogger = Substitute.For<IHttpInboundRequestLogger>();

            ILogger<RuntimeStateMiddleware> logger = Substitute.For<ILogger<RuntimeStateMiddleware>>();

            return new RuntimeStateMiddleware(next, configuration, inboundRequestLogger, logger);
        }

        private static DefaultHttpContext CreateContext(string method, string path)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = method;
            context.Request.Path = path.Split('?')[0];
            context.Request.QueryString = path.Contains('?')
                ? new QueryString(path[path.IndexOf('?')..])
                : QueryString.Empty;
            context.Response.Body = new MemoryStream();
            return context;
        }
    }
}
