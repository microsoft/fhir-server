// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Resources.Create;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Delete
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class ConditionalDeleteResourceValidatorTests
    {
        private readonly ConditionalDeleteResourceValidator _validator;

        public ConditionalDeleteResourceValidatorTests()
        {
            var modelInfoProvider = Substitute.For<IModelInfoProvider>();
            modelInfoProvider.IsKnownResource(KnownResourceTypes.Observation).Returns(true);
            _validator = new ConditionalDeleteResourceValidator(
                Options.Create(new CoreFeatureConfiguration()),
                modelInfoProvider,
                NullLogger<ConditionalDeleteResourceValidator>.Instance);
        }

        public static IEnumerable<object[]> InclusionParameters()
        {
            foreach (var parameter in new[]
            {
                "_include", "_include:iterate", "_include:recurse", "_include:unknown",
                "_revinclude", "_revinclude:iterate", "_revinclude:recurse", "_revinclude:unknown",
                "_INCLUDE", "_REVINCLUDE:ITERATE",
            })
            {
                foreach (int? count in new int?[] { null, 1, 2 })
                {
                    foreach (var operation in new[] { DeleteOperation.SoftDelete, DeleteOperation.HardDelete })
                    {
                        yield return new object[] { parameter, count, operation };
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(InclusionParameters))]
        public async Task GivenInclusionParameter_WhenValidatingPipeline_ThenRequestIsRejectedBeforeHandlerRuns(
            string parameter,
            int? count,
            DeleteOperation operation)
        {
            // Arrange
            var request = new ConditionalDeleteResourceRequest(
                KnownResourceTypes.Observation,
                new[] { Tuple.Create("_id", "observation-id"), Tuple.Create(parameter, "Observation:subject") },
                operation,
                count);
            var pipeline = new ValidateRequestPreProcessor<ConditionalDeleteResourceRequest, DeleteResourceResponse>(new[] { _validator });
            bool handlerCalled = false;

            // Act
            var exception = await Assert.ThrowsAsync<ResourceNotValidException>(() => pipeline.HandleAsync(
                request,
                () =>
                {
                    handlerCalled = true;
                    return Task.FromResult(new DeleteResourceResponse(0));
                },
                CancellationToken.None));

            // Assert
            Assert.False(handlerCalled);
            var issue = Assert.Single(exception.Issues);
            Assert.Equal(
                string.Format(CultureInfo.InvariantCulture, Core.Resources.ConditionalDeleteIncludesNotSupported, parameter),
                issue.Diagnostics);
        }

        [Theory]
        [InlineData("_include")]
        [InlineData("_revinclude:iterate")]
        public void GivenOnlyInclusionParameter_WhenValidating_ThenRequestIsRejected(string parameter)
        {
            // Arrange
            var request = new ConditionalDeleteResourceRequest(
                KnownResourceTypes.Observation,
                new[] { Tuple.Create(parameter, "Observation:subject") },
                DeleteOperation.SoftDelete,
                1);

            // Act
            var result = _validator.Validate(request);

            // Assert
            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
        }

        [Theory]
        [InlineData(null, DeleteOperation.SoftDelete)]
        [InlineData(1, DeleteOperation.SoftDelete)]
        [InlineData(2, DeleteOperation.SoftDelete)]
        [InlineData(1, DeleteOperation.HardDelete)]
        [InlineData(2, DeleteOperation.HardDelete)]
        public void GivenMatchingCriteriaWithoutInclusions_WhenValidating_ThenRequestIsAccepted(int? count, DeleteOperation operation)
        {
            // Arrange
            var request = new ConditionalDeleteResourceRequest(
                KnownResourceTypes.Observation,
                new[] { Tuple.Create("_id", "observation-id"), Tuple.Create("subject:Patient", "patient-id") },
                operation,
                count);

            // Act
            var result = _validator.Validate(request);

            // Assert
            Assert.True(result.IsValid);
        }
    }
}
