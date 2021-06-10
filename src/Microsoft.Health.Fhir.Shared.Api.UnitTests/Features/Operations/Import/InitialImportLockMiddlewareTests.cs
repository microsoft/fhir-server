// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Configs;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Operations.Import
{
    public class InitialImportLockMiddlewareTests
    {
        [Fact]
        public async Task GivenPostResourceRequest_WhenInitalImportModeEnabled_Then423ShouldBeReturned()
        {
            InitialImportLockMiddleware middleware = CreateInitialImportLockMiddleware(new ImportTaskConfiguration() { Enabled = true, InitalImportMode = true });
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/Patient";
            httpContext.Request.Method = HttpMethods.Post.ToString();
            await middleware.Invoke(httpContext);

            Assert.Equal(423, httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task GivenGetResourceRequest_WhenInitalImportModeEnabled_Then200ShouldBeReturned()
        {
            InitialImportLockMiddleware middleware = CreateInitialImportLockMiddleware(new ImportTaskConfiguration() { Enabled = false, InitalImportMode = true });
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/patient";
            httpContext.Request.Method = HttpMethods.Get.ToString();
            await middleware.Invoke(httpContext);

            Assert.Equal(200, httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task GivenStartImportRequest_WhenInitalImportModeEnabled_Then200ShouldBeReturned()
        {
            InitialImportLockMiddleware middleware = CreateInitialImportLockMiddleware(new ImportTaskConfiguration() { Enabled = false, InitalImportMode = true });
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/$import";
            httpContext.Request.Method = HttpMethods.Post.ToString();
            await middleware.Invoke(httpContext);

            Assert.Equal(200, httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task GivenCancelImportRequest_WhenInitalImportModeEnabled_Then200ShouldBeReturned()
        {
            InitialImportLockMiddleware middleware = CreateInitialImportLockMiddleware(new ImportTaskConfiguration() { Enabled = false, InitalImportMode = true });
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/_operations/import/abc";
            httpContext.Request.Method = HttpMethods.Delete.ToString();
            await middleware.Invoke(httpContext);

            Assert.Equal(200, httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task GivenPostResourceRequest_WhenImportNotEnabled_Then200ShouldBeReturned()
        {
            InitialImportLockMiddleware middleware = CreateInitialImportLockMiddleware(new ImportTaskConfiguration() { Enabled = false, InitalImportMode = true });
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/Patient";
            httpContext.Request.Method = HttpMethods.Post.ToString();
            await middleware.Invoke(httpContext);

            Assert.Equal(200, httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task GivenPostResourceRequest_WhenInitalImportModeNotEnabled_Then200ShouldBeReturned()
        {
            InitialImportLockMiddleware middleware = CreateInitialImportLockMiddleware(new ImportTaskConfiguration() { Enabled = true, InitalImportMode = false });
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/Patient";
            httpContext.Request.Method = HttpMethods.Post.ToString();
            await middleware.Invoke(httpContext);

            Assert.Equal(200, httpContext.Response.StatusCode);
        }

        private InitialImportLockMiddleware CreateInitialImportLockMiddleware(ImportTaskConfiguration importTaskConfiguration)
        {
            return new InitialImportLockMiddleware(
                    async x =>
                    {
                        x.Response.StatusCode = 200;
                        await Task.CompletedTask;
                    },
                    Options.Create(importTaskConfiguration));
        }
    }
}
