// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Api.Features.BackgroundJobService;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.JobManagement;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.BackgroundJobService
{
    public sealed class JobMetricFactoryTests
    {
        private readonly IJobMetricFactory _jobMetricFactory;

        public JobMetricFactoryTests()
        {
            _jobMetricFactory = new JobMetricFactory(
                Substitute.For<IBulkDeleteMetricHandler>(),
                Substitute.For<IBulkUpdateMetricHandler>(),
                Substitute.For<IExportMetricHandler>(),
                Substitute.For<IImportMetricHandler>(),
                Substitute.For<IReindexMetricHandler>());
        }

        [Fact]
        public void GivenAJobMetricFactory_WhenProcessingBulkDelete_ReturnSupportedMetrics()
        {
            IJobMetric jobMetric = GetJobMetric(JobType.BulkDeleteOrchestrator);
            Assert.Null(jobMetric);

            jobMetric = GetJobMetric(JobType.BulkDeleteProcessing);
            Assert.NotNull(jobMetric);
            Assert.True(jobMetric is IBulkDeleteMetricHandler);
        }

        [Fact]
        public void GivenAJobMetricFactory_WhenProcessingBulkUpdate_ReturnSupportedMetrics()
        {
            IJobMetric jobMetric = GetJobMetric(JobType.BulkUpdateOrchestrator);
            Assert.Null(jobMetric);

            jobMetric = GetJobMetric(JobType.BulkUpdateProcessing);
            Assert.NotNull(jobMetric);
            Assert.True(jobMetric is IBulkUpdateMetricHandler);
        }

        [Fact]
        public void GivenAJobMetricFactory_WhenProcessingExport_ReturnSupportedMetrics()
        {
            IJobMetric jobMetric = GetJobMetric(JobType.ExportOrchestrator);
            Assert.NotNull(jobMetric);
            Assert.True(jobMetric is IExportMetricHandler);

            jobMetric = GetJobMetric(JobType.ExportProcessing);
            Assert.NotNull(jobMetric);
            Assert.True(jobMetric is IExportMetricHandler);
        }

        [Fact]
        public void GivenAJobMetricFactory_WhenProcessingImport_ReturnSupportedMetrics()
        {
            IJobMetric jobMetric = GetJobMetric(JobType.ImportOrchestrator);
            Assert.NotNull(jobMetric);
            Assert.True(jobMetric is IImportMetricHandler);

            jobMetric = GetJobMetric(JobType.ImportProcessing);
            Assert.NotNull(jobMetric);
            Assert.True(jobMetric is IImportMetricHandler);
        }

        [Fact]
        public void GivenAJobMetricFactory_WhenProcessingReindex_ReturnSupportedMetrics()
        {
            IJobMetric jobMetric = GetJobMetric(JobType.ReindexOrchestrator);
            Assert.NotNull(jobMetric);
            Assert.True(jobMetric is IReindexMetricHandler);

            jobMetric = GetJobMetric(JobType.ReindexProcessing);
            Assert.Null(jobMetric);
        }

        private IJobMetric GetJobMetric(JobType jobType)
        {
            int jobTypeId = (int)jobType;
            string jobDefinition = $"{{ typeId:{jobTypeId} }}";

            JobInfo jobInfo = new JobInfo()
            {
                Definition = jobDefinition,
            };

            IJobMetric jobMetric = _jobMetricFactory.Create(jobInfo);

            return jobMetric;
        }
    }
}
