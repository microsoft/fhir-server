// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Export)]
    public class ExportOrchestratorJobTests
    {
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();

        [Fact]
        public void GivenAllValidResourceTypes_WhenValidating_ThenNoExceptionIsThrown()
        {
            _searchService.IsValidResourceType("Patient").Returns(true);
            _searchService.IsValidResourceType("Observation").Returns(true);

            var record = CreateRecord("Patient,Observation");
            var resourceTypes = new[] { "Patient", "Observation" };

            TestableExportOrchestratorJob.InvokeValidateResourceTypes(_searchService, resourceTypes, record);

            Assert.Null(record.FailureDetails);
        }

        [Fact]
        public void GivenAnInvalidResourceType_WhenValidating_ThenJobExecutionExceptionWithBadRequestIsThrown()
        {
            _searchService.IsValidResourceType("Patient").Returns(true);
            _searchService.IsValidResourceType("InvalidType").Returns(false);

            var record = CreateRecord("Patient,InvalidType");
            var resourceTypes = new[] { "Patient", "InvalidType" };

            var ex = Assert.Throws<JobExecutionException>(
                () => TestableExportOrchestratorJob.InvokeValidateResourceTypes(_searchService, resourceTypes, record));

            Assert.Contains("InvalidType", ex.Message);
            Assert.NotNull(record.FailureDetails);
            Assert.Equal(HttpStatusCode.BadRequest, record.FailureDetails.FailureStatusCode);
        }

        [Fact]
        public void GivenMultipleInvalidResourceTypes_WhenValidating_ThenExceptionMessageContainsAllInvalidTypes()
        {
            _searchService.IsValidResourceType(Arg.Any<string>()).Returns(false);

            var record = CreateRecord("FakeTypeA,FakeTypeB");
            var resourceTypes = new[] { "FakeTypeA", "FakeTypeB" };

            var ex = Assert.Throws<JobExecutionException>(
                () => TestableExportOrchestratorJob.InvokeValidateResourceTypes(_searchService, resourceTypes, record));

            Assert.Contains("FakeTypeA", ex.Message);
            Assert.Contains("FakeTypeB", ex.Message);
            Assert.Equal(HttpStatusCode.BadRequest, record.FailureDetails.FailureStatusCode);
        }

        [Fact]
        public void GivenNullResourceTypes_WhenValidating_ThenNoExceptionIsThrown()
        {
            var record = CreateRecord(null);

            TestableExportOrchestratorJob.InvokeValidateResourceTypes(_searchService, null, record);

            Assert.Null(record.FailureDetails);
        }

        [Fact]
        public void GivenEmptyResourceTypes_WhenValidating_ThenNoExceptionIsThrown()
        {
            var record = CreateRecord(null);

            TestableExportOrchestratorJob.InvokeValidateResourceTypes(_searchService, Array.Empty<string>(), record);

            Assert.Null(record.FailureDetails);
        }

        [Fact]
        public void GivenWhitespaceResourceType_WhenValidating_ThenExceptionMessageContainsEmptyPlaceholder()
        {
            var record = CreateRecord(" ");
            var resourceTypes = new[] { " " };

            var ex = Assert.Throws<JobExecutionException>(
                () => TestableExportOrchestratorJob.InvokeValidateResourceTypes(_searchService, resourceTypes, record));

            Assert.Contains("<empty>", ex.Message);
            Assert.Equal(HttpStatusCode.BadRequest, record.FailureDetails.FailureStatusCode);
        }

        [Fact]
        public void GivenConcatenatedResourceTypes_WhenValidating_ThenJobExecutionExceptionWithBadRequestIsThrown()
        {
            // Simulates the ASP.NET model binding bug where _type=Specimen&_type=Person becomes "SpecimenPerson"
            _searchService.IsValidResourceType("SpecimenPerson").Returns(false);

            var record = CreateRecord("SpecimenPerson");
            var resourceTypes = new[] { "SpecimenPerson" };

            var ex = Assert.Throws<JobExecutionException>(
                () => TestableExportOrchestratorJob.InvokeValidateResourceTypes(_searchService, resourceTypes, record));

            Assert.Contains("SpecimenPerson", ex.Message);
            Assert.Equal(HttpStatusCode.BadRequest, record.FailureDetails.FailureStatusCode);
        }

        private static ExportJobRecord CreateRecord(string resourceType)
        {
            return new ExportJobRecord(
                new Uri("http://localhost/ExportJob"),
                ExportJobType.All,
                ExportFormatTags.ResourceName,
                resourceType,
                filters: null,
                hash: "hash",
                rollingFileSizeInMB: 64);
        }

        /// <summary>
        /// Concrete subclass to expose the protected static ValidateResourceTypes for testing.
        /// </summary>
        private class TestableExportOrchestratorJob : ExportOrchestratorJob
        {
            public override Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public static void InvokeValidateResourceTypes(ISearchService searchService, IEnumerable<string> resourceTypes, ExportJobRecord record)
            {
                ValidateResourceTypes(searchService, resourceTypes, record);
            }
        }
    }
}
