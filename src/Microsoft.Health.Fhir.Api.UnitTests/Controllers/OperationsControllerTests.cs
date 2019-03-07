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
    public class OperationsControllerTests
    {
        private OperationsController GetController(OperationsConfiguration operationsConfig)
        {
            IOptions<OperationsConfiguration> optionsConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            optionsConfiguration.Value.Returns(operationsConfig);

            return new OperationsController(
                NullLogger<OperationsController>.Instance,
                Substitute.For<IFhirRequestContextAccessor>(),
                optionsConfiguration);
        }

        [Fact]
        public void GivenSupportsExportIsDisabled_WhenRequestingExport_ThenBadRequestResponseShouldBeReturned()
        {
            var opController = GetController(new OperationsConfiguration() { SupportsBulkExport = false });

            var result = opController.Export() as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        }

        [Fact]
        public void GivenSupportsExportIsEnabled_WhenRequestingExport_ThenNotImplementedResponseShouldBeReturned()
        {
            var opController = GetController(new OperationsConfiguration() { SupportsBulkExport = true });

            var result = opController.Export() as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.NotImplemented, result.StatusCode);
        }

        [Fact]
        public void GivenRequestingExportByResourceType_WhenResourceTypeIsNotPatient_ThenBadRequestResponseShouldBeReturned()
        {
            var opController = GetController(new OperationsConfiguration() { SupportsBulkExport = true });

            var result = opController.ExportResourceType("Observation") as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        }

        [Fact]
        public void GivenRequestingExportByResourceType_WhenResourceTypeIsPatient_ThenNotImplementedResponseShouldBeReturned()
        {
            var opController = GetController(new OperationsConfiguration() { SupportsBulkExport = true });

            var result = opController.ExportResourceType("Patient") as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.NotImplemented, result.StatusCode);
        }

        [Fact]
        public void GivenRequestingExportByResourceTypeId_WhenResourceTypeIsNotGroup_ThenBadRequestResponseShouldBeReturned()
        {
            var opController = GetController(new OperationsConfiguration() { SupportsBulkExport = true });

            var result = opController.ExportResourceTypeById("Patient", "id") as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        }

        [Fact]
        public void GivenRequestingExportByResourceTypeId_WhenResourceTypeIsGroup_ThenNotImplementedResponseShouldBeReturned()
        {
            var opController = GetController(new OperationsConfiguration() { SupportsBulkExport = true });

            var result = opController.ExportResourceTypeById("Group", "id") as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.NotImplemented, result.StatusCode);
        }
    }
}
