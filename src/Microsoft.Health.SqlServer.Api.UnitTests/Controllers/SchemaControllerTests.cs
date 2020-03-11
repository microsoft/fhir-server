// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.SqlServer.Api.Controllers;
using Microsoft.Health.SqlServer.Features.Schema;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.SqlServer.Api.UnitTests.Controllers
{
    public class SchemaControllerTests
    {
        private readonly SchemaController<TestSchemaVersion> _schemaController;

        public SchemaControllerTests()
        {
            var schemaInformation = Substitute.For<ISchemaInformation>();

            var urlHelperFactory = Substitute.For<IUrlHelperFactory>();
            var urlHelper = Substitute.For<IUrlHelper>();
            urlHelper.RouteUrl(Arg.Any<UrlRouteContext>()).Returns("https://localhost/script");
            urlHelperFactory.GetUrlHelper(Arg.Any<ActionContext>()).Returns(urlHelper);

            var actionContextAccessor = Substitute.For<IActionContextAccessor>();
            actionContextAccessor.ActionContext.Returns(new ActionContext());

            _schemaController = new SchemaController<TestSchemaVersion>(schemaInformation, urlHelperFactory, actionContextAccessor, NullLogger<SchemaController<TestSchemaVersion>>.Instance);
        }

        [Fact]
        public void GivenAScriptRequest_WhenSchemaIdFound_ThenReturnScriptSuccess()
        {
            ActionResult result = _schemaController.SqlScript(1);
            string script = result.ToString();
            Assert.NotNull(script);
        }

        [Fact]
        public void GivenAnAvailableVersionsRequest_WhenCurrentVersionIsNull_ThenAllVersionsReturned()
        {
            ActionResult result = _schemaController.AvailableVersions();

            var jsonResult = result as JsonResult;
            Assert.NotNull(jsonResult);

            var jArrayResult = JArray.FromObject(jsonResult.Value);
            Assert.Equal(Enum.GetNames(typeof(TestSchemaVersion)).Length, jArrayResult.Count);

            JToken firstResult = jArrayResult.First;
            Assert.Equal(1, firstResult["id"]);
            Assert.Equal("https://localhost/script", firstResult["script"]);
        }

        [Fact]
        public void GivenACurrentVersionRequest_WhenNotImplemented_ThenNotImplementedShouldBeThrown()
        {
            Assert.Throws<NotImplementedException>(() => _schemaController.CurrentVersion());
        }

        [Fact]
        public void GivenACompatibilityRequest_WhenNotImplemented_ThenNotImplementedShouldBeThrown()
        {
            Assert.Throws<NotImplementedException>(() => _schemaController.Compatibility());
        }
    }
}
