// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.SqlServer.Api.Controllers;
using Microsoft.Health.Fhir.SqlServer.Api.Features.Routing;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.Api.UnitTests.Controllers
{
    public class SchemaControllerTests
    {
        private readonly SchemaController _schemaController;

        public SchemaControllerTests()
        {
            var schemaInformation = new SchemaInformation();
            var urlResolver = Substitute.For<IUrlResolver>();
            urlResolver.ResolveRouteNameUrl(RouteNames.Script, Arg.Any<IDictionary<string, object>>()).Returns(new Uri("https://localhost/script"));
            _schemaController = new SchemaController(schemaInformation, urlResolver, NullLogger<SchemaController>.Instance);
        }

        [Fact]
        public void GivenAScriptRequest_WhenNotImplemented_ThenNotImplementedShouldBeThrown()
        {
            Assert.Throws<NotImplementedException>(() => _schemaController.SqlScript(0));
        }

        [Fact]
        public void GivenAnAvailableVersionsRequest_WhenCurrentVersionIsNull_ThenAllVersionsReturned()
        {
            ActionResult result = _schemaController.AvailableVersions();

            var jsonResult = result as JsonResult;
            Assert.NotNull(jsonResult);

            var jArrayResult = JArray.FromObject(jsonResult.Value);
            Assert.Equal(Enum.GetNames(typeof(SchemaVersion)).Length, jArrayResult.Count);

            JToken firstResult = jArrayResult.First;
            Assert.Equal(1, firstResult["id"]);
            Assert.Equal("https://localhost/script", firstResult["script"]);
        }

        [Fact]
        public void GivenACurrentVersiontRequest_WhenNotImplemented_ThenNotImplementedShouldBeThrown()
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
