// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Persistence
{
    public class SqlServerTaskConsumerTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private SqlServerFhirStorageTestsFixture _fixture;

        public SqlServerTaskConsumerTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task GivenASqlConsumer_WhenGetNextTask_AvailableTasksShouldBeReturned()
        {
            string queueId = Guid.NewGuid().ToString();
            TaskHostingConfiguration config = new TaskHostingConfiguration()
            {
                EnableBulkImport = true,
                QueueId = queueId,
                TaskHeartbeatTimeoutThresholdInSeconds = 60,
            };

            IOptions<TaskHostingConfiguration> optionsExportConfig = Substitute.For<IOptions<TaskHostingConfiguration>>();
            optionsExportConfig.Value.Returns(config);
            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerTaskManager>.Instance);
            SqlServerTaskConsumer sqlServerTaskConsumer = new SqlServerTaskConsumer(optionsExportConfig, _fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerTaskConsumer>.Instance);

            for (int i = 0; i < 5; ++i)
            {
                string taskId = Guid.NewGuid().ToString();
                short typeId = 1;
                string inputData = "inputData";

                TaskInfo taskInfo = new TaskInfo()
                {
                    TaskId = taskId,
                    QueueId = queueId,
                    TaskTypeId = typeId,
                    InputData = inputData,
                };

                _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None);
            }

            var result = (await sqlServerTaskConsumer.GetNextMessagesAsync(3, 60, CancellationToken.None)).ToList();
            Assert.Equal(3, result.Count());
        }
    }
}
