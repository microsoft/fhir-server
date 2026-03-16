// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Operation;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class OperationDefinitionControllerTests
    {
        private readonly OperationDefinitionController _controller;
        private readonly OperationsConfiguration _operationConfiguration;
        private readonly FeatureConfiguration _featureConfiguration;
        private readonly CoreFeatureConfiguration _coreFeatureConfiguration;
        private readonly ImplementationGuidesConfiguration _implementationGuidesConfiguration;
        private readonly IFhirRuntimeConfiguration _fhirRuntimeConfiguration;
        private readonly IMediator _mediator;

        public OperationDefinitionControllerTests()
        {
            _mediator = Substitute.For<IMediator>();
            _mediator.Send<OperationDefinitionResponse>(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new OperationDefinitionResponse(new OperationDefinition().ToResourceElement()));

            _operationConfiguration = new OperationsConfiguration();
            _featureConfiguration = new FeatureConfiguration();
            _coreFeatureConfiguration = new CoreFeatureConfiguration();
            _implementationGuidesConfiguration = new ImplementationGuidesConfiguration();
            _fhirRuntimeConfiguration = Substitute.For<IFhirRuntimeConfiguration>();

            _controller = new OperationDefinitionController(
                _mediator,
                Options.Create(_operationConfiguration),
                Options.Create(_featureConfiguration),
                Options.Create(_coreFeatureConfiguration),
                Options.Create(_implementationGuidesConfiguration),
                _fhirRuntimeConfiguration);
            _controller.ControllerContext = new ControllerContext(
                new ActionContext(
                    Substitute.For<HttpContext>(),
                    new RouteData(),
                    new ControllerActionDescriptor()));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenReindexIsEnabled_ThenOperationDefinitionShouldBeReturned(bool enabled)
        {
            _operationConfiguration.Reindex.Enabled = enabled;
            try
            {
                await _controller.ReindexOperationDefinition();
                Assert.True(enabled);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled);
            }

            await _mediator.Received(enabled ? 1 : 0).Send<OperationDefinitionResponse>(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenResourceReindexIsEnabled_ThenOperationDefinitionShouldBeReturned(bool enabled)
        {
            _operationConfiguration.Reindex.Enabled = enabled;
            try
            {
                await _controller.ResourceReindexOperationDefinition();
                Assert.True(enabled);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled);
            }

            await _mediator.Received(enabled ? 1 : 0).Send<OperationDefinitionResponse>(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenExportIsEnabled_ThenOperationDefinitionShouldBeReturned(bool enabled)
        {
            _operationConfiguration.Export.Enabled = enabled;
            try
            {
                await _controller.ExportOperationDefinition();
                Assert.True(enabled);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled);
            }

            await _mediator.Received(enabled ? 1 : 0).Send<OperationDefinitionResponse>(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenPatientExportIsEnabled_ThenOperationDefinitionShouldBeReturned(bool enabled)
        {
            _operationConfiguration.Export.Enabled = enabled;
            try
            {
                await _controller.PatientExportOperationGetDefinition();
                Assert.True(enabled);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled);
            }

            await _mediator.Received(enabled ? 1 : 0).Send<OperationDefinitionResponse>(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenGroupExportIsEnabled_ThenOperationDefinitionShouldBeReturned(bool enabled)
        {
            _operationConfiguration.Export.Enabled = enabled;
            try
            {
                await _controller.GroupExportOperationDefinition();
                Assert.True(enabled);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled);
            }

            await _mediator.Received(enabled ? 1 : 0).Send<OperationDefinitionResponse>(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenAnonymizedExportIsEnabled_ThenOperationDefinitionShouldBeReturned(bool enabled)
        {
            _featureConfiguration.SupportsAnonymizedExport = enabled;
            try
            {
                await _controller.AnonymizedExportOperationDefinition();
                Assert.True(enabled);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled);
            }

            await _mediator.Received(enabled ? 1 : 0).Send<OperationDefinitionResponse>(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenConvertDataIsEnabled_ThenOperationDefinitionShouldBeReturned(bool enabled)
        {
            _operationConfiguration.ConvertData.Enabled = enabled;
            try
            {
                await _controller.ConvertDataOperationDefinition();
                Assert.True(enabled);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled);
            }

            await _mediator.Received(enabled ? 1 : 0).Send<OperationDefinitionResponse>(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenConfiguration_WhenMemberMatchIsEnabled_ThenOperationDefinitionShouldBeReturned()
        {
            // Always enabled
            await _controller.MemberMatchOperationDefinition();

            await _mediator.Received(1).Send<OperationDefinitionResponse>(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenConfiguration_WhenPurgeHistoryIsEnabled_ThenOperationDefinitionShouldBeReturned()
        {
            // Always enabled
            await _controller.PurgeHistoryOperationDefinition();

            await _mediator.Received(1).Send<OperationDefinitionResponse>(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenBulkDeleteIsEnabled_ThenOperationDefinitionShouldBeReturned(bool enabled)
        {
            _operationConfiguration.BulkDelete.Enabled = enabled;
            try
            {
                await _controller.BulkDeleteOperationDefinition();
                Assert.True(enabled);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled);
            }

            await _mediator.Received(enabled ? 1 : 0).Send<OperationDefinitionResponse>(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenConfiguration_WhenBulkDeleteSoftDeletedIsEnabled_ThenOperationDefinitionShouldBeReturned()
        {
            // Always disabled
            await Assert.ThrowsAnyAsync<RequestNotValidException>(() => _controller.BulkDeleteSoftDeletedOperationDefinition());

            await _mediator.DidNotReceive().Send<OperationDefinitionResponse>(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenBulkUpdateIsEnabled_ThenOperationDefinitionShouldBeReturned(bool enabled)
        {
            _operationConfiguration.BulkUpdate.Enabled = enabled;
            try
            {
                await _controller.BulkUpdateOperationDefinition();
                Assert.True(enabled);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled);
            }

            await _mediator.Received(enabled ? 1 : 0).Send<OperationDefinitionResponse>(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenSearchParameterStatusIsEnabled_ThenOperationDefinitionShouldBeReturned(bool enabled)
        {
            _coreFeatureConfiguration.SupportsSelectableSearchParameters = enabled;
            try
            {
                await _controller.SearchParameterStatusOperationDefintion();
                Assert.True(enabled);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled);
            }

            await _mediator.Received(enabled ? 1 : 0).Send<OperationDefinitionResponse>(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true, KnownDataStores.SqlServer)]
        [InlineData(false, KnownDataStores.SqlServer)]
        [InlineData(true, KnownDataStores.CosmosDb)]
        [InlineData(false, KnownDataStores.CosmosDb)]
        [InlineData(true, null)]
        [InlineData(false, null)]
        public async Task GivenConfiguration_WhenIncludesIsEnabled_ThenOperationDefinitionShouldBeReturned(
            bool enabled,
            string dataStore)
        {
            _coreFeatureConfiguration.SupportsIncludes = enabled;
            _fhirRuntimeConfiguration.DataStore.Returns(dataStore);
            try
            {
                await _controller.IncludesOperationDefinition();
                Assert.True(enabled);
                Assert.Equal(KnownDataStores.SqlServer, dataStore, StringComparer.OrdinalIgnoreCase);
            }
            catch (SearchOperationNotSupportedException)
            {
                Assert.NotEqual(KnownDataStores.SqlServer, dataStore, StringComparer.OrdinalIgnoreCase);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled);
            }

            await _mediator.Received(enabled && string.Equals(dataStore, KnownDataStores.SqlServer, StringComparison.OrdinalIgnoreCase) ? 1 : 0).Send<OperationDefinitionResponse>(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenDocRefIsEnabled_ThenOperationDefinitionShouldBeReturned(bool enabled)
        {
            _implementationGuidesConfiguration.USCore.EnableDocRef = enabled;
            try
            {
                await _controller.DocRefOperationDefinition();
                Assert.True(enabled);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled);
            }

            await _mediator.Received(enabled ? 1 : 0).Send<OperationDefinitionResponse>(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenExpandIsEnabled_ThenOperationDefinitionShouldBeReturned(bool enabled)
        {
            _operationConfiguration.Terminology.EnableExpand = enabled;
            try
            {
                await _controller.ExpandOperationDefinition();
                Assert.True(enabled);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled);
            }

            await _mediator.Received(enabled ? 1 : 0).Send<OperationDefinitionResponse>(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>());
        }
    }
}
