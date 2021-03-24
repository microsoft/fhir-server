// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.BulkImport;
using Microsoft.Health.Fhir.Core.Models;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    public class BulkImportControllerTests
    {
        private BulkImportController _bulkImportEnabledController;
        private IMediator _mediator = Substitute.For<IMediator>();
        private IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private IUrlResolver _urlResolver = Substitute.For<IUrlResolver>();

        public BulkImportControllerTests()
        {
            _bulkImportEnabledController = GetController(new BulkImportJobConfiguration() { Enabled = true });
        }

        public static TheoryData<BulkImportRequestConfiguration> ValidBody =>
            new TheoryData<BulkImportRequestConfiguration>
            {
                GetValidBulkImportRequestConfiguration(),
            };

        public static TheoryData<BulkImportRequestConfiguration> InValidBody =>
            new TheoryData<BulkImportRequestConfiguration>
            {
                GetBulkImportRequestConfigurationWithUnsupportedInputFormat(),
                GetBulkImportRequestConfigurationWithUnsupportedStorageType(),
                GetBulkImportRequestConfigurationWithUnsupportedResourceType(),
            };

        [Theory]
        [MemberData(nameof(ValidBody), MemberType = typeof(BulkImportControllerTests))]
        public async Task GivenAnBulkImportRequest_WhenDisabled_ThenRequestNotValidExceptionShouldBeThrown(BulkImportRequestConfiguration body)
        {
            var bulkImportController = GetController(new BulkImportJobConfiguration() { Enabled = false });

            await Assert.ThrowsAsync<RequestNotValidException>(() => bulkImportController.BulkImport(body));
        }

        [Theory]
        [MemberData(nameof(InValidBody), MemberType = typeof(BulkImportControllerTests))]
        public async Task GivenAnBulkImportRequest_WhenRequestConfigurationNotValid_ThenRequestNotValidExceptionShouldBeThrown(BulkImportRequestConfiguration body)
        {
            var bulkImportController = GetController(new BulkImportJobConfiguration() { Enabled = true });

            await Assert.ThrowsAsync<RequestNotValidException>(() => bulkImportController.BulkImport(body));
        }

        private static CreateBulkImportResponse CreateBulkImportResponse()
        {
            return new CreateBulkImportResponse("123");
        }

        private BulkImportController GetController(BulkImportJobConfiguration bulkImportConfig)
        {
            var operationConfig = new OperationsConfiguration()
            {
                BulkImport = bulkImportConfig,
            };

            IOptions<OperationsConfiguration> optionsOperationConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            optionsOperationConfiguration.Value.Returns(operationConfig);

            return new BulkImportController(
                _mediator,
                _fhirRequestContextAccessor,
                _urlResolver,
                optionsOperationConfiguration,
                NullLogger<BulkImportController>.Instance);
        }

        private static BulkImportRequestConfiguration GetValidBulkImportRequestConfiguration()
        {
            var input = new List<BulkImportRequestInputConfiguration>
            {
                new BulkImportRequestInputConfiguration
                {
                    Type = "Patient",
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson?sig=RHIX5Xcg0Mq2rqI3OlWT"),
                },
                new BulkImportRequestInputConfiguration
                {
                    Type = "Observation",
                    Url = new Uri("https://client.example.org/obseration_file_19.ndjson?sig=RHIX5Xcg0Mq2rqI3OlWT"),
                },
            };

            var bulkImportRequestConfiguration = new BulkImportRequestConfiguration();
            bulkImportRequestConfiguration.InputFormat = "application/fhir+ndjson";
            bulkImportRequestConfiguration.InputSource = new Uri("https://other-server.example.org");
            bulkImportRequestConfiguration.Input = input;
            bulkImportRequestConfiguration.StorageDetail = new BulkImportRequestStorageDetailConfiguration();

            return bulkImportRequestConfiguration;
        }

        private static BulkImportRequestConfiguration GetBulkImportRequestConfigurationWithUnsupportedInputFormat()
        {
            var input = new List<BulkImportRequestInputConfiguration>
            {
                new BulkImportRequestInputConfiguration
                {
                    Type = "Patient",
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson?sig=RHIX5Xcg0Mq2rqI3OlWT"),
                },
            };

            var bulkImportRequestConfiguration = new BulkImportRequestConfiguration();
            bulkImportRequestConfiguration.InputFormat = "application/json";
            bulkImportRequestConfiguration.InputSource = new Uri("https://other-server.example.org");
            bulkImportRequestConfiguration.Input = input;

            return bulkImportRequestConfiguration;
        }

        private static BulkImportRequestConfiguration GetBulkImportRequestConfigurationWithUnsupportedStorageType()
        {
            var input = new List<BulkImportRequestInputConfiguration>
            {
                new BulkImportRequestInputConfiguration
                {
                    Type = "Patient",
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson?sig=RHIX5Xcg0Mq2rqI3OlWT"),
                },
            };

            var bulkImportRequestConfiguration = new BulkImportRequestConfiguration();
            bulkImportRequestConfiguration.InputFormat = "application/fhir+ndjson";
            bulkImportRequestConfiguration.InputSource = new Uri("https://other-server.example.org");
            bulkImportRequestConfiguration.Input = input;
            bulkImportRequestConfiguration.StorageDetail = new BulkImportRequestStorageDetailConfiguration
            {
                Type = "Fake",
            };

            return bulkImportRequestConfiguration;
        }

        private static BulkImportRequestConfiguration GetBulkImportRequestConfigurationWithUnsupportedResourceType()
        {
            var input = new List<BulkImportRequestInputConfiguration>
            {
                new BulkImportRequestInputConfiguration
                {
                    Type = "Fake",
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson?sig=RHIX5Xcg0Mq2rqI3OlWT"),
                },
            };

            var bulkImportRequestConfiguration = new BulkImportRequestConfiguration();
            bulkImportRequestConfiguration.InputFormat = "application/fhir+ndjson";
            bulkImportRequestConfiguration.InputSource = new Uri("https://other-server.example.org");
            bulkImportRequestConfiguration.Input = input;

            return bulkImportRequestConfiguration;
        }
    }
}
