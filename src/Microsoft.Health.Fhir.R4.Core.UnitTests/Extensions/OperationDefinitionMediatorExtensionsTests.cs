// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.Operation;
using Microsoft.Health.Fhir.Shared.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.R4.Core.UnitTests.Extensions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class OperationDefinitionMediatorExtensionsTests
    {
        private readonly IMediator _mediator;

        public OperationDefinitionMediatorExtensionsTests()
        {
            _mediator = Substitute.For<IMediator>();
        }

        [Theory]
        [InlineData("export")]
        [InlineData("reindex")]
        [InlineData("member-match")]
        [InlineData("convert-data")]
        [InlineData("purge-history")]
        public async Task GivenVariousOperationNames_WhenGetOperationDefinitionAsync_ThenCorrectRequestIsSent(string operationName)
        {
            // Arrange
            var operationDefinition = CreateTestOperationDefinition(operationName);
            var response = new OperationDefinitionResponse(operationDefinition.ToResourceElement());

            _mediator.Send(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(response);

            // Act
            await _mediator.GetOperationDefinitionAsync(operationName, CancellationToken.None);

            // Assert
            await _mediator.Received(1).Send(
                Arg.Is<OperationDefinitionRequest>(r => r.OperationName == operationName),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenCancellationToken_WhenGetOperationDefinitionAsync_ThenCancellationTokenIsPassedThrough()
        {
            // Arrange
            const string operationName = "export";
            var operationDefinition = CreateTestOperationDefinition(operationName);
            var response = new OperationDefinitionResponse(operationDefinition.ToResourceElement());
            var cancellationToken = new CancellationToken(false);

            _mediator.Send(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(response);

            // Act
            await _mediator.GetOperationDefinitionAsync(operationName, cancellationToken);

            // Assert
            await _mediator.Received(1).Send(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Is<CancellationToken>(ct => ct == cancellationToken));
        }

        [Fact]
        public async Task GivenNullMediator_WhenGetOperationDefinitionAsync_ThenArgumentNullExceptionIsThrown()
        {
            // Arrange
            IMediator nullMediator = null;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => nullMediator.GetOperationDefinitionAsync("export", CancellationToken.None));
        }

        [Fact]
        public async Task GivenNullOperationName_WhenGetOperationDefinitionAsync_ThenArgumentExceptionIsThrown()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => _mediator.GetOperationDefinitionAsync(null, CancellationToken.None));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GivenInvalidOperationName_WhenGetOperationDefinitionAsync_ThenArgumentExceptionIsThrown(string operationName)
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(
                () => _mediator.GetOperationDefinitionAsync(operationName, CancellationToken.None));
        }

        [Fact]
        public async Task GivenMediatorThrowsException_WhenGetOperationDefinitionAsync_ThenExceptionIsNotCaught()
        {
            // Arrange
            const string operationName = "export";
            _mediator.Send(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>())
                .Returns<OperationDefinitionResponse>(x => throw new InvalidOperationException("Test exception"));

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _mediator.GetOperationDefinitionAsync(operationName, CancellationToken.None));
        }

        [Fact]
        public async Task GivenCancellationRequested_WhenGetOperationDefinitionAsync_ThenOperationCanceledExceptionIsThrown()
        {
            // Arrange
            const string operationName = "export";
            var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            _mediator.Send(
                Arg.Any<OperationDefinitionRequest>(),
                Arg.Any<CancellationToken>())
                .Returns<OperationDefinitionResponse>(x => throw new OperationCanceledException());

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => _mediator.GetOperationDefinitionAsync(operationName, cancellationTokenSource.Token));
        }

        [Fact]
        public async Task GivenMultipleCalls_WhenGetOperationDefinitionAsync_ThenEachCallCreatesNewRequest()
        {
            // Arrange
            const string operationName1 = "export";
            const string operationName2 = "reindex";

            var operationDefinition1 = CreateTestOperationDefinition(operationName1);
            var operationDefinition2 = CreateTestOperationDefinition(operationName2);

            _mediator.Send(
                Arg.Is<OperationDefinitionRequest>(r => r.OperationName == operationName1),
                Arg.Any<CancellationToken>())
                .Returns(new OperationDefinitionResponse(operationDefinition1.ToResourceElement()));

            _mediator.Send(
                Arg.Is<OperationDefinitionRequest>(r => r.OperationName == operationName2),
                Arg.Any<CancellationToken>())
                .Returns(new OperationDefinitionResponse(operationDefinition2.ToResourceElement()));

            // Act
            var result1 = await _mediator.GetOperationDefinitionAsync(operationName1, CancellationToken.None);
            var result2 = await _mediator.GetOperationDefinitionAsync(operationName2, CancellationToken.None);

            // Assert
            Assert.NotNull(result1);
            Assert.NotNull(result2);
            await _mediator.Received(1).Send(
                Arg.Is<OperationDefinitionRequest>(r => r.OperationName == operationName1),
                Arg.Any<CancellationToken>());
            await _mediator.Received(1).Send(
                Arg.Is<OperationDefinitionRequest>(r => r.OperationName == operationName2),
                Arg.Any<CancellationToken>());
        }

        private static OperationDefinition CreateTestOperationDefinition(string operationName)
        {
            return new OperationDefinition
            {
                Url = $"http://example.org/fhir/OperationDefinition/{operationName}",
                Name = operationName,
                Status = PublicationStatus.Active,
                Kind = OperationDefinition.OperationKind.Operation,
                Code = operationName,
                System = false,
                Type = true,
                Instance = false,
                Parameter = new System.Collections.Generic.List<OperationDefinition.ParameterComponent>(),
            };
        }
    }
}
