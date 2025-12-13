// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Messages;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkUpdate)]
    public class BulkUpdateControllerTests
    {
        private readonly BulkUpdateController _controller;
        private readonly OperationsConfiguration _operationConfiguration;
        private readonly IFhirRuntimeConfiguration _fhirRuntimeConfiguration;
        private readonly IMediator _mediator;

        public BulkUpdateControllerTests()
        {
            _mediator = Substitute.For<IMediator>();
            _mediator.Send<CreateBulkUpdateResponse>(
                Arg.Any<CreateBulkUpdateRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new CreateBulkUpdateResponse(0));
            _mediator.Send<GetBulkUpdateResponse>(
                Arg.Any<GetBulkUpdateRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new GetBulkUpdateResponse(
                    new List<Parameters.ParameterComponent>(),
                    new List<OperationOutcomeIssue>(),
                    HttpStatusCode.Accepted));
            _mediator.Send<CancelBulkUpdateResponse>(
                Arg.Any<CancelBulkUpdateRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new CancelBulkUpdateResponse(HttpStatusCode.OK));

            var urlResolver = Substitute.For<IUrlResolver>();
            urlResolver.ResolveOperationResultUrl(
                Arg.Any<string>(),
                Arg.Any<string>())
                .Returns(new Uri("https://test"));

            _operationConfiguration = new OperationsConfiguration();
            _fhirRuntimeConfiguration = Substitute.For<IFhirRuntimeConfiguration>();
            _controller = new BulkUpdateController(
                _mediator,
                urlResolver,
                Options.Create(_operationConfiguration),
                _fhirRuntimeConfiguration);
            _controller.ControllerContext = new ControllerContext(
                new ActionContext(
                    Substitute.For<HttpContext>(),
                    new RouteData(),
                    new ControllerActionDescriptor()));
        }

        [Theory]
        [InlineData(true, KnownDataStores.SqlServer)]
        [InlineData(false, KnownDataStores.SqlServer)]
        [InlineData(true, KnownDataStores.CosmosDb)]
        [InlineData(false, KnownDataStores.CosmosDb)]
        [InlineData(true, null)]
        [InlineData(false, null)]
        public async Task GivenConfiguration_WhenBulkUpdateIsEnabled_ThenCreateBulkUpdateShouldSucceed(
            bool enabled,
            string dataStore)
        {
            _operationConfiguration.BulkUpdate.Enabled = enabled;
            _fhirRuntimeConfiguration.DataStore.Returns(dataStore);
            try
            {
                await _controller.BulkUpdate(new Parameters());
                Assert.True(enabled);
                Assert.Equal(KnownDataStores.SqlServer, dataStore, StringComparer.OrdinalIgnoreCase);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled && string.Equals(dataStore, KnownDataStores.SqlServer, StringComparison.OrdinalIgnoreCase));
            }

            await _mediator.Received(enabled && string.Equals(dataStore, KnownDataStores.SqlServer, StringComparison.OrdinalIgnoreCase) ? 1 : 0).Send<CreateBulkUpdateResponse>(
                Arg.Any<CreateBulkUpdateRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true, KnownDataStores.SqlServer)]
        [InlineData(false, KnownDataStores.SqlServer)]
        [InlineData(true, KnownDataStores.CosmosDb)]
        [InlineData(false, KnownDataStores.CosmosDb)]
        [InlineData(true, null)]
        [InlineData(false, null)]
        public async Task GivenConfiguration_WhenBulkUpdateIsEnabled_ThenCreateBulkUpdateByResourceTypeShouldSucceed(
            bool enabled,
            string dataStore)
        {
            _operationConfiguration.BulkUpdate.Enabled = enabled;
            _fhirRuntimeConfiguration.DataStore.Returns(dataStore);
            try
            {
                await _controller.BulkUpdateByResourceType(KnownResourceTypes.Patient, new Parameters());
                Assert.True(enabled);
                Assert.Equal(KnownDataStores.SqlServer, dataStore, StringComparer.OrdinalIgnoreCase);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled && string.Equals(dataStore, KnownDataStores.SqlServer, StringComparison.OrdinalIgnoreCase));
            }

            await _mediator.Received(enabled && string.Equals(dataStore, KnownDataStores.SqlServer, StringComparison.OrdinalIgnoreCase) ? 1 : 0).Send<CreateBulkUpdateResponse>(
                Arg.Any<CreateBulkUpdateRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true, KnownDataStores.SqlServer)]
        [InlineData(false, KnownDataStores.SqlServer)]
        [InlineData(true, KnownDataStores.CosmosDb)]
        [InlineData(false, KnownDataStores.CosmosDb)]
        [InlineData(true, null)]
        [InlineData(false, null)]
        public async Task GivenConfiguration_WhenBulkUpdateIsEnabled_ThenGetBulkUpdateStatusByIdShouldSucceed(
            bool enabled,
            string dataStore)
        {
            _operationConfiguration.BulkUpdate.Enabled = enabled;
            _fhirRuntimeConfiguration.DataStore.Returns(dataStore);
            try
            {
                await _controller.GetBulkUpdateStatusById(0);
                Assert.True(enabled);
                Assert.Equal(KnownDataStores.SqlServer, dataStore, StringComparer.OrdinalIgnoreCase);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled && string.Equals(dataStore, KnownDataStores.SqlServer, StringComparison.OrdinalIgnoreCase));
            }

            await _mediator.Received(enabled && string.Equals(dataStore, KnownDataStores.SqlServer, StringComparison.OrdinalIgnoreCase) ? 1 : 0).Send<GetBulkUpdateResponse>(
                Arg.Any<GetBulkUpdateRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true, KnownDataStores.SqlServer)]
        [InlineData(false, KnownDataStores.SqlServer)]
        [InlineData(true, KnownDataStores.CosmosDb)]
        [InlineData(false, KnownDataStores.CosmosDb)]
        [InlineData(true, null)]
        [InlineData(false, null)]
        public async Task GivenConfiguration_WhenBulkUpdateIsEnabled_ThenCancelBulkUpdateShouldSucceed(
            bool enabled,
            string dataStore)
        {
            _operationConfiguration.BulkUpdate.Enabled = enabled;
            _fhirRuntimeConfiguration.DataStore.Returns(dataStore);
            try
            {
                await _controller.CancelBulkUpdate(0);
                Assert.True(enabled);
                Assert.Equal(KnownDataStores.SqlServer, dataStore, StringComparer.OrdinalIgnoreCase);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled && string.Equals(dataStore, KnownDataStores.SqlServer, StringComparison.OrdinalIgnoreCase));
            }

            await _mediator.Received(enabled && string.Equals(dataStore, KnownDataStores.SqlServer, StringComparison.OrdinalIgnoreCase) ? 1 : 0).Send<CancelBulkUpdateResponse>(
                Arg.Any<CancelBulkUpdateRequest>(),
                Arg.Any<CancellationToken>());
        }
    }
}
