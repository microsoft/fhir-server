// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Routing;
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
        public async void GivenAnExportRequest_WhenDisabled_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var exportController = GetController(new ExportConfiguration() { Enabled = false });

            await Assert.ThrowsAsync<RequestNotValidException>(() => exportController.Export());
        }

        [Fact]
        public void GivenAnExportByResourceTypeRequest_WhenResourceTypeIsNotPatient_ThenRequestNotValidExceptionShouldBeThrown()
        {
            Assert.Throws<RequestNotValidException>(() => _exportEnabledController.ExportResourceType(ResourceType.Observation.ToString()));
        }

        [Fact]
        public void GivenAnExportByResourceTypeRequest_WhenResourceTypeIsPatient_ThenOperationNotImplementedExceptionShouldBeThrown()
        {
            Assert.Throws<OperationNotImplementedException>(() => _exportEnabledController.ExportResourceType(ResourceType.Patient.ToString()));
        }

        [Fact]
        public void GivenAnExportResourceTypeIdRequest_WhenResourceTypeIsNotGroup_ThenRequestNotValidExceptionShouldBeThrown()
        {
            Assert.Throws<RequestNotValidException>(() => _exportEnabledController.ExportResourceTypeById(ResourceType.Patient.ToString(), "id"));
        }

        [Fact]
        public void GivenAnExportByResourceTypeIdRequest_WhenResourceTypeIsGroup_ThenOperationNotImplementedExceptionShouldBeThrown()
        {
            Assert.Throws<OperationNotImplementedException>(() => _exportEnabledController.ExportResourceTypeById(ResourceType.Group.ToString(), "id"));
        }

        private ExportController GetController(
            ExportConfiguration exportConfig,
            IMediator mediator = null,
            IFhirRequestContextAccessor fhirRequestContextAccessor = null,
            IUrlResolver urlResolver = null)
        {
            var operationConfig = new OperationsConfiguration()
            {
                Export = exportConfig,
            };

            IOptions<OperationsConfiguration> optionsOperationConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            optionsOperationConfiguration.Value.Returns(operationConfig);

            return new ExportController(
                mediator ?? Substitute.For<IMediator>(),
                fhirRequestContextAccessor ?? Substitute.For<IFhirRequestContextAccessor>(),
                urlResolver ?? Substitute.For<IUrlResolver>(),
                optionsOperationConfiguration,
                NullLogger<ExportController>.Instance);
        }
    }
}
