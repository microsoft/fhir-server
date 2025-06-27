// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Operations.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class InitialImportLockMiddlewareTests
    {
        [Fact]
        public async Task GivenPostResourceRequest_WhenInitialImportModeEnabled_Then423ShouldBeReturned()
        {
            InitialImportLockMiddleware middleware = CreateInitialImportLockMiddleware(new ImportJobConfiguration() { Enabled = true, InitialImportMode = true });
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/Patient";
            httpContext.Request.Method = HttpMethods.Post.ToString();
            await middleware.Invoke(httpContext);

            Assert.Equal(423, httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task GivenCustomErrorRequest_WhenInitialImportModeEnabled_Then423ShouldNotBeReturned()
        {
            InitialImportLockMiddleware middleware = CreateInitialImportLockMiddleware(new ImportJobConfiguration() { Enabled = true, InitialImportMode = true });
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/CustomError";
            httpContext.Request.Method = HttpMethods.Post.ToString();
            await middleware.Invoke(httpContext);

            Assert.Equal(200, httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task GivenGetResourceRequest_WhenInitialImportModeEnabled_Then200ShouldBeReturned()
        {
            InitialImportLockMiddleware middleware = CreateInitialImportLockMiddleware(new ImportJobConfiguration() { Enabled = false, InitialImportMode = true });
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/patient";
            httpContext.Request.Method = HttpMethods.Get.ToString();
            await middleware.Invoke(httpContext);

            Assert.Equal(200, httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task GivenStartImportRequest_WhenInitialImportModeEnabled_Then200ShouldBeReturned()
        {
            InitialImportLockMiddleware middleware = CreateInitialImportLockMiddleware(new ImportJobConfiguration() { Enabled = false, InitialImportMode = true });
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/$import";
            httpContext.Request.Method = HttpMethods.Post.ToString();
            await middleware.Invoke(httpContext);

            Assert.Equal(200, httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task GivenImportRequestWithPrefix_WhenInitialImportModeEnabled_Then200ShouldBeReturned()
        {
            InitialImportLockMiddleware middleware = CreateInitialImportLockMiddleware(new ImportJobConfiguration() { Enabled = false, InitialImportMode = true });
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/prefix/$import";
            httpContext.Request.Method = HttpMethods.Post.ToString();
            await middleware.Invoke(httpContext);

            Assert.Equal(200, httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task GivenCancelImportRequest_WhenInitialImportModeEnabled_Then200ShouldBeReturned()
        {
            InitialImportLockMiddleware middleware = CreateInitialImportLockMiddleware(new ImportJobConfiguration() { Enabled = false, InitialImportMode = true });
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/_operations/import/abc";
            httpContext.Request.Method = HttpMethods.Delete.ToString();
            await middleware.Invoke(httpContext);

            Assert.Equal(200, httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task GivenCancelImportRequestWithPrefix_WhenInitialImportModeEnabled_Then200ShouldBeReturned()
        {
            InitialImportLockMiddleware middleware = CreateInitialImportLockMiddleware(new ImportJobConfiguration() { Enabled = false, InitialImportMode = true });
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/prefix/_operations/import/abc";
            httpContext.Request.Method = HttpMethods.Delete.ToString();
            await middleware.Invoke(httpContext);

            Assert.Equal(200, httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task GivenPostResourceRequest_WhenImportNotEnabled_Then200ShouldBeReturned()
        {
            InitialImportLockMiddleware middleware = CreateInitialImportLockMiddleware(new ImportJobConfiguration() { Enabled = false, InitialImportMode = true });
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/Patient";
            httpContext.Request.Method = HttpMethods.Post.ToString();
            await middleware.Invoke(httpContext);

            Assert.Equal(200, httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task GivenPostResourceRequest_WhenInitialImportModeNotEnabled_Then200ShouldBeReturned()
        {
            InitialImportLockMiddleware middleware = CreateInitialImportLockMiddleware(new ImportJobConfiguration() { Enabled = true, InitialImportMode = false });
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/Patient";
            httpContext.Request.Method = HttpMethods.Post.ToString();
            await middleware.Invoke(httpContext);

            Assert.Equal(200, httpContext.Response.StatusCode);
        }

        [Theory]
        [InlineData("/Patient", "Post")]
        [InlineData("/$reindex", "Get")]
        [InlineData("/Observation", "Delete")]
        public async Task GivenLockedRequests_WhenInitialImportModeEnabled_Then423ShouldBeReturned(string path, string method)
        {
            InitialImportLockMiddleware middleware = CreateInitialImportLockMiddleware(new ImportJobConfiguration() { Enabled = true, InitialImportMode = true });
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = path;
            httpContext.Request.Method = method;
            await middleware.Invoke(httpContext);

            Assert.Equal(423, httpContext.Response.StatusCode);
        }

        [Theory]
        [InlineData("/Patient", "Get")]
        [InlineData("/$export", "Get")]
        [InlineData("/Patient/$export", "Get")]
        [InlineData("/_operations/export/123", "Get")]
        [InlineData("/_operations/export/123", "Delete")]
        [InlineData("/_operations/reindex/123", "Get")]
        [InlineData("/_operations/reindex/123", "Delete")]
        [InlineData("/_operations/import/123", "Get")]
        [InlineData("/_operations/import/123", "Delete")]
        public async Task GivenAllowedRequests_WhenInitialImportModeEnabled_Then200ShouldBeReturned(string path, string method)
        {
            InitialImportLockMiddleware middleware = CreateInitialImportLockMiddleware(new ImportJobConfiguration() { Enabled = true, InitialImportMode = true });
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = path;
            httpContext.Request.Method = method;
            await middleware.Invoke(httpContext);

            Assert.Equal(200, httpContext.Response.StatusCode);
        }

        private InitialImportLockMiddleware CreateInitialImportLockMiddleware(ImportJobConfiguration importJobConfiguration)
        {
            return new InitialImportLockMiddleware(
                    async x =>
                    {
                        x.Response.StatusCode = 200;
                        await Task.CompletedTask;
                    },
                    Options.Create(importJobConfiguration));
        }
    }
}
