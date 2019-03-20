// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    public class ExportControllerTests
    {
        private ExportController _exportEnabledController;

        public ExportControllerTests()
        {
            _exportEnabledController = GetController(new ExportConfiguration() { Enabled = true });
        }

        [Fact]
        public void GivenAnExportRequest_WhenDisabled_ThenBadRequestResponseShouldBeReturned()
        {
            var exportController = GetController(new ExportConfiguration() { Enabled = false });

            Assert.Throws<RequestNotValidException>(() => exportController.Export());
        }

        [Fact]
        public void GivenAnExportRequest_WhenEnabled_ThenNotImplementedResponseShouldBeReturned()
        {
            var result = _exportEnabledController.Export() as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.NotImplemented, result.StatusCode);
        }

        [Fact]
        public void GivenAnExportByResourceTypeRequest_WhenResourceTypeIsNotPatient_ThenBadRequestResponseShouldBeReturned()
        {
            Assert.Throws<RequestNotValidException>(() => _exportEnabledController.ExportResourceType(ResourceType.Observation.ToString()));
        }

        [Fact]
        public void GivenAnExportByResourceTypeRequest_WhenResourceTypeIsPatient_ThenNotImplementedResponseShouldBeReturned()
        {
            var result = _exportEnabledController.ExportResourceType(ResourceType.Patient.ToString()) as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.NotImplemented, result.StatusCode);
        }

        [Fact]
        public void GivenAnExportResourceTypeIdRequest_WhenResourceTypeIsNotGroup_ThenBadRequestResponseShouldBeReturned()
        {
            Assert.Throws<RequestNotValidException>(() => _exportEnabledController.ExportResourceTypeById(ResourceType.Patient.ToString(), "id"));
        }

        [Fact]
        public void GivenAnExportByResourceTypeIdRequest_WhenResourceTypeIsGroup_ThenNotImplementedResponseShouldBeReturned()
        {
            var result = _exportEnabledController.ExportResourceTypeById(ResourceType.Group.ToString(), "id") as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.NotImplemented, result.StatusCode);
        }

        private ExportController GetController(ExportConfiguration exportConfig)
        {
            IOptions<ExportConfiguration> exportConfiguration = Substitute.For<IOptions<ExportConfiguration>>();
            exportConfiguration.Value.Returns(exportConfig);

            return new ExportController(
                Substitute.For<IFhirRequestContextAccessor>(),
                exportConfiguration,
                NullLogger<ExportController>.Instance);
        }
    }
}
