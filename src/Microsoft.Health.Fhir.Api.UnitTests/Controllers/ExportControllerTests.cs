// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    public class ExportControllerTests
    {
        [Fact]
        public void GivenAnExportRequest_WhenDisabled_ThenBadRequestResponseShouldBeReturned()
        {
            var exportController = GetController(new ExportConfiguration() { Enabled = false });

            var result = exportController.Export() as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        }

        [Fact]
        public void GivenAnExportRequest_WhenEnabled_ThenNotImplementedResponseShouldBeReturned()
        {
            var exportController = GetController(new ExportConfiguration() { Enabled = true });

            var result = exportController.Export() as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.NotImplemented, result.StatusCode);
        }

        [Fact]
        public void GivenAnExportByResourceTypeRequest_WhenResourceTypeIsNotPatient_ThenBadRequestResponseShouldBeReturned()
        {
            var exportController = GetController(new ExportConfiguration() { Enabled = true });

            var result = exportController.ExportResourceType("Observation") as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        }

        [Fact]
        public void GivenAnExportByResourceTypeRequest_WhenResourceTypeIsPatient_ThenNotImplementedResponseShouldBeReturned()
        {
            var exportController = GetController(new ExportConfiguration() { Enabled = true });

            var result = exportController.ExportResourceType("Patient") as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.NotImplemented, result.StatusCode);
        }

        [Fact]
        public void GivenAnExportResourceTypeIdRequest_WhenResourceTypeIsNotGroup_ThenBadRequestResponseShouldBeReturned()
        {
            var exportController = GetController(new ExportConfiguration() { Enabled = true });

            var result = exportController.ExportResourceTypeById("Patient", "id") as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        }

        [Fact]
        public void GivenAnExportByResourceTypeIdRequest_WhenResourceTypeIsGroup_ThenNotImplementedResponseShouldBeReturned()
        {
            var exportController = GetController(new ExportConfiguration() { Enabled = true });

            var result = exportController.ExportResourceTypeById("Group", "id") as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.NotImplemented, result.StatusCode);
        }

        private ExportController GetController(ExportConfiguration exportConfig)
        {
            IOptions<ExportConfiguration> exportConfiguration = Substitute.For<IOptions<ExportConfiguration>>();
            exportConfiguration.Value.Returns(exportConfig);

            return new ExportController(
                NullLogger<ExportController>.Instance,
                Substitute.For<IFhirRequestContextAccessor>(),
                exportConfiguration);
        }
    }
}
