# Stale Job Monitor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Emit a per-queue-type Prometheus gauge (`fhir_oldest_queued_job_age_seconds{queue_type=...}`) showing the age in seconds of the oldest queued job when no jobs are running, enabling alerting on stalled job queues with `> 600`.

**Architecture:** `StaleJobWatchdog` (Watchdog<T> subclass, 60s poll, singleton lease) queries `SqlQueueClient.GetActiveJobsByQueueTypeAsync` for every `QueueType` except `Unknown`, computes per-queue oldest-job age, logs stalled queues, and publishes `StaleJobMetricsNotification` via MediatR. `StaleJobMetricHandler` holds a `ConcurrentDictionary<QueueType, double>` and exposes values via `ObservableGauge<double>` with `queue_type` label. `WatchdogsBackgroundService` is modified to inject and start `StaleJobWatchdog`.

**Tech Stack:** C# / .NET 8, `System.Diagnostics.Metrics` (ObservableGauge), MediatR, NSubstitute (tests), xUnit

---

## File Map

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `src/Microsoft.Health.Fhir.Core/Features/Operations/StaleJob/Messages/StaleJobMetricsNotification.cs` | MediatR notification carrying per-queue ages |
| Create | `src/Microsoft.Health.Fhir.Core/Logging/Metrics/StaleJobMetricHandler.cs` | Handles notification, updates ConcurrentDictionary, exposes ObservableGauge |
| Create | `src/Microsoft.Health.Fhir.SqlServer/Features/Watchdogs/StaleJobWatchdog.cs` | Polls queue, computes ages, publishes notification |
| Modify | `src/Microsoft.Health.Fhir.SqlServer/Features/Watchdogs/WatchdogsBackgroundService.cs` | Add StaleJobWatchdog field, constructor param, and task |
| Modify | `src/Microsoft.Health.Fhir.SqlServer/Registration/FhirServerBuilderSqlServerRegistrationExtensions.cs` | Register watchdog and handler as singletons |
| Create | `test/Microsoft.Health.Fhir.Shared.Tests.Integration/Persistence/StaleJobWatchdogLogicTests.cs` | Logic-only tests for ComputeQueueAges (no SQL) |
| Modify | `test/Microsoft.Health.Fhir.Shared.Tests.Integration/Persistence/SqlServerWatchdogTests.cs` | Integration test: watchdog runs and publishes notification |

---

## Task 1: StaleJobMetricsNotification

**Files:**
- Create: `src/Microsoft.Health.Fhir.Core/Features/Operations/StaleJob/Messages/StaleJobMetricsNotification.cs`

- [ ] **Step 1: Create the notification class**

```csharp
// src/Microsoft.Health.Fhir.Core/Features/Operations/StaleJob/Messages/StaleJobMetricsNotification.cs
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Features.Operations.StaleJob.Messages
{
    public class StaleJobMetricsNotification : INotification
    {
        public StaleJobMetricsNotification(IReadOnlyDictionary<QueueType, double> queueAges)
        {
            QueueAges = EnsureArg.IsNotNull(queueAges, nameof(queueAges));
        }

        public IReadOnlyDictionary<QueueType, double> QueueAges { get; }
    }
}
```

- [ ] **Step 2: Build to verify no errors**

```bash
cd /c/Src/fhir-server
dotnet build src/Microsoft.Health.Fhir.Core/Microsoft.Health.Fhir.Core.csproj --no-restore -p:TreatWarningsAsErrors=false 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/Microsoft.Health.Fhir.Core/Features/Operations/StaleJob/Messages/StaleJobMetricsNotification.cs
git commit -m "feat: add StaleJobMetricsNotification"
```

---

## Task 2: StaleJobMetricHandler

**Files:**
- Create: `src/Microsoft.Health.Fhir.Core/Logging/Metrics/StaleJobMetricHandler.cs`

TDD note: `StaleJobMetricHandler` is pure in-memory — no SQL or HTTP. Tests live in Task 3's logic test file to avoid an extra file for two tests.

- [ ] **Step 1: Implement StaleJobMetricHandler**

```csharp
// src/Microsoft.Health.Fhir.Core/Logging/Metrics/StaleJobMetricHandler.cs
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.StaleJob.Messages;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public sealed class StaleJobMetricHandler : BaseMeterMetricHandler, INotificationHandler<StaleJobMetricsNotification>
    {
        private readonly ObservableGauge<double> _gauge;

        internal readonly ConcurrentDictionary<QueueType, double> QueueAges = new();

        public StaleJobMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _gauge = MetricMeter.CreateObservableGauge("fhir_oldest_queued_job_age_seconds", ObserveValues);
        }

        public Task Handle(StaleJobMetricsNotification notification, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            foreach (var (queueType, age) in notification.QueueAges)
            {
                QueueAges[queueType] = age;
            }

            return Task.CompletedTask;
        }

        private IEnumerable<Measurement<double>> ObserveValues()
        {
            return QueueAges.Select(kv => new Measurement<double>(
                kv.Value,
                new KeyValuePair<string, object?>("queue_type", kv.Key.ToString())));
        }
    }
}
```

- [ ] **Step 2: Build to verify no errors**

```bash
dotnet build src/Microsoft.Health.Fhir.Core/Microsoft.Health.Fhir.Core.csproj --no-restore -p:TreatWarningsAsErrors=false 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/Microsoft.Health.Fhir.Core/Logging/Metrics/StaleJobMetricHandler.cs
git commit -m "feat: add StaleJobMetricHandler with per-queue ObservableGauge"
```

---

## Task 3: StaleJobWatchdog + logic tests

**Files:**
- Create: `src/Microsoft.Health.Fhir.SqlServer/Features/Watchdogs/StaleJobWatchdog.cs`
- Create: `test/Microsoft.Health.Fhir.Shared.Tests.Integration/Persistence/StaleJobWatchdogLogicTests.cs`

- [ ] **Step 1: Write the failing logic tests**

```csharp
// test/Microsoft.Health.Fhir.Shared.Tests.Integration/Persistence/StaleJobWatchdogLogicTests.cs
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.StaleJob.Messages;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.TaskManagement;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class StaleJobWatchdogLogicTests
    {
        private static DateTime _now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void ComputeQueueAges_WhenAnyJobRunning_EmitsZeroForAllQueues()
        {
            var jobs = new Dictionary<QueueType, IReadOnlyList<JobInfo>>
            {
                [QueueType.Export] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Running, CreateDate = _now.AddMinutes(-15) },
                },
                [QueueType.Import] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Created, CreateDate = _now.AddMinutes(-20) },
                },
            };

            var result = StaleJobWatchdog.ComputeQueueAges(jobs, _now);

            Assert.Equal(0, result[QueueType.Export]);
            Assert.Equal(0, result[QueueType.Import]);
        }

        [Fact]
        public void ComputeQueueAges_WhenNoJobsRunning_EmitsOldestCreatedJobAge()
        {
            var jobs = new Dictionary<QueueType, IReadOnlyList<JobInfo>>
            {
                [QueueType.Export] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Created, CreateDate = _now.AddSeconds(-754) },
                    new JobInfo { Status = JobStatus.Created, CreateDate = _now.AddSeconds(-200) },
                },
                [QueueType.Import] = new List<JobInfo>(),
            };

            var result = StaleJobWatchdog.ComputeQueueAges(jobs, _now);

            Assert.Equal(754, result[QueueType.Export]);
            Assert.Equal(0, result[QueueType.Import]);
        }

        [Fact]
        public void ComputeQueueAges_WhenQueueEmpty_EmitsZero()
        {
            var jobs = new Dictionary<QueueType, IReadOnlyList<JobInfo>>
            {
                [QueueType.Export] = new List<JobInfo>(),
            };

            var result = StaleJobWatchdog.ComputeQueueAges(jobs, _now);

            Assert.Equal(0, result[QueueType.Export]);
        }

        [Fact]
        public async Task StaleJobMetricHandler_Handle_UpdatesQueueAges()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IMeterFactory>();
            var handler = new StaleJobMetricHandler(factory);

            var ages = new Dictionary<QueueType, double>
            {
                [QueueType.Export] = 754.0,
                [QueueType.Import] = 0.0,
            };

            await handler.Handle(new StaleJobMetricsNotification(ages), CancellationToken.None);

            Assert.Equal(754.0, handler.QueueAges[QueueType.Export]);
            Assert.Equal(0.0, handler.QueueAges[QueueType.Import]);
        }

        [Fact]
        public async Task StaleJobMetricHandler_Handle_OverwritesPreviousValues()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IMeterFactory>();
            var handler = new StaleJobMetricHandler(factory);

            await handler.Handle(
                new StaleJobMetricsNotification(new Dictionary<QueueType, double> { [QueueType.Export] = 500.0 }),
                CancellationToken.None);

            await handler.Handle(
                new StaleJobMetricsNotification(new Dictionary<QueueType, double> { [QueueType.Export] = 0.0 }),
                CancellationToken.None);

            Assert.Equal(0.0, handler.QueueAges[QueueType.Export]);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd /c/Src/fhir-server
dotnet build test/Microsoft.Health.Fhir.R4.Tests.Integration/ --no-restore -p:TreatWarningsAsErrors=false 2>&1 | grep -E "error|Error" | head -10
```

Expected: Build error — `StaleJobWatchdog` does not exist.

- [ ] **Step 3: Implement StaleJobWatchdog**

```csharp
// src/Microsoft.Health.Fhir.SqlServer/Features/Watchdogs/StaleJobWatchdog.cs
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.StaleJob.Messages;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.TaskManagement;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal sealed class StaleJobWatchdog : Watchdog<StaleJobWatchdog>
    {
        private readonly SqlQueueClient _queueClient;
        private readonly IMediator _mediator;
        private readonly ILogger<StaleJobWatchdog> _logger;

        public StaleJobWatchdog(
            ISqlRetryService sqlRetryService,
            ILogger<StaleJobWatchdog> logger,
            SqlQueueClient queueClient,
            IMediator mediator)
            : base(sqlRetryService, logger)
        {
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        internal StaleJobWatchdog()
        {
            // used to get param names for testing
        }

        public override double LeasePeriodSec { get; internal set; } = 300;

        public override bool AllowRebalance { get; internal set; } = true;

        public override double PeriodSec { get; internal set; } = 60;

        protected override async Task RunWorkAsync(CancellationToken cancellationToken)
        {
            var jobsByQueue = new Dictionary<QueueType, IReadOnlyList<JobInfo>>();

            foreach (var queueType in Enum.GetValues<QueueType>().Where(q => q != QueueType.Unknown))
            {
                var jobs = await _queueClient.GetActiveJobsByQueueTypeAsync((byte)queueType, false, cancellationToken);
                jobsByQueue[queueType] = jobs;
            }

            var ages = ComputeQueueAges(jobsByQueue, DateTime.UtcNow);

            foreach (var (queueType, age) in ages.Where(kv => kv.Value > 0))
            {
                _logger.LogWarning(
                    "Stale job queue detected. QueueType={QueueType} OldestJobAgeSecs={Age}",
                    queueType,
                    age);
            }

            await _mediator.Publish(new StaleJobMetricsNotification(ages), cancellationToken);
        }

        internal static Dictionary<QueueType, double> ComputeQueueAges(
            Dictionary<QueueType, IReadOnlyList<JobInfo>> jobsByQueue,
            DateTime utcNow)
        {
            bool anyRunning = jobsByQueue.Values
                .SelectMany(j => j)
                .Any(j => j.Status == JobStatus.Running);

            var result = new Dictionary<QueueType, double>();

            foreach (var (queueType, jobs) in jobsByQueue)
            {
                if (anyRunning)
                {
                    result[queueType] = 0;
                    continue;
                }

                var createdJobs = jobs.Where(j => j.Status == JobStatus.Created).ToList();
                result[queueType] = createdJobs.Count > 0
                    ? (utcNow - createdJobs.Min(j => j.CreateDate)).TotalSeconds
                    : 0;
            }

            return result;
        }
    }
}
```

- [ ] **Step 4: Run the logic tests**

```bash
dotnet test test/Microsoft.Health.Fhir.R4.Tests.Integration/ --filter "FullyQualifiedName~StaleJobWatchdogLogicTests" --no-build 2>&1 | tail -10
```

Expected: 5 tests passed.

- [ ] **Step 5: Commit**

```bash
git add src/Microsoft.Health.Fhir.SqlServer/Features/Watchdogs/StaleJobWatchdog.cs \
        test/Microsoft.Health.Fhir.Shared.Tests.Integration/Persistence/StaleJobWatchdogLogicTests.cs
git commit -m "feat: add StaleJobWatchdog with per-queue age computation"
```

---

## Task 4: WatchdogsBackgroundService

**Files:**
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Features/Watchdogs/WatchdogsBackgroundService.cs`

- [ ] **Step 1: Add the StaleJobWatchdog field**

Add after line 31 (`private readonly GeoReplicationLagWatchdog _geoReplicationLagWatchdog;`):

```csharp
        private readonly StaleJobWatchdog _staleJobWatchdog;
```

- [ ] **Step 2: Add the constructor parameter and assignment**

Add `StaleJobWatchdog staleJobWatchdog` as the last parameter before `IOptions<CoreFeatureConfiguration>`:

```csharp
        public WatchdogsBackgroundService(
            DefragWatchdog defragWatchdog,
            CleanupEventLogWatchdog cleanupEventLogWatchdog,
            IScopeProvider<TransactionWatchdog> transactionWatchdog,
            InvisibleHistoryCleanupWatchdog invisibleHistoryCleanupWatchdog,
            ExpiredResourceCleanupWatchdog expiredResourceCleanupWatchdog,
            GeoReplicationLagWatchdog geoReplicationLagWatchdog,
            StaleJobWatchdog staleJobWatchdog,
            IOptions<CoreFeatureConfiguration> coreFeatureConfiguration,
            IOptions<WatchdogConfiguration> watchdogConfiguration)
```

Add the assignment after line 50 (`_geoReplicationLagWatchdog = geoReplicationLagWatchdog;`):

```csharp
            _staleJobWatchdog = EnsureArg.IsNotNull(staleJobWatchdog, nameof(staleJobWatchdog));
```

- [ ] **Step 3: Add StaleJobWatchdog to the tasks list**

Add after line 71 (`_invisibleHistoryCleanupWatchdog.ExecuteAsync(continuationTokenSource.Token),`) in `ExecuteAsync`:

```csharp
                _staleJobWatchdog.ExecuteAsync(continuationTokenSource.Token),
```

The `tasks` initializer should now look like:

```csharp
            var tasks = new List<Task>
            {
                _defragWatchdog.ExecuteAsync(continuationTokenSource.Token),
                _cleanupEventLogWatchdog.ExecuteAsync(continuationTokenSource.Token),
                _transactionWatchdog.Value.ExecuteAsync(continuationTokenSource.Token),
                _invisibleHistoryCleanupWatchdog.ExecuteAsync(continuationTokenSource.Token),
                _staleJobWatchdog.ExecuteAsync(continuationTokenSource.Token),
            };
```

- [ ] **Step 4: Build to verify no errors**

```bash
dotnet build src/Microsoft.Health.Fhir.SqlServer/Microsoft.Health.Fhir.SqlServer.csproj --no-restore -p:TreatWarningsAsErrors=false 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/Microsoft.Health.Fhir.SqlServer/Features/Watchdogs/WatchdogsBackgroundService.cs
git commit -m "feat: add StaleJobWatchdog to WatchdogsBackgroundService"
```

---

## Task 5: DI Registration

**Files:**
- Modify: `src/Microsoft.Health.Fhir.SqlServer/Registration/FhirServerBuilderSqlServerRegistrationExtensions.cs`

- [ ] **Step 1: Find the insertion point**

```bash
grep -n "GeoReplicationLagWatchdog\|RemoveServiceTypeExact.*WatchdogsBackgroundService" \
  src/Microsoft.Health.Fhir.SqlServer/Registration/FhirServerBuilderSqlServerRegistrationExtensions.cs
```

Expected: two line numbers — GeoReplicationLagWatchdog singleton registration (~192) and the WatchdogsBackgroundService RemoveServiceTypeExact block (~194).

- [ ] **Step 2: Add registrations between GeoReplicationLagWatchdog and WatchdogsBackgroundService**

Add the following two blocks after the `services.Add<GeoReplicationLagWatchdog>().Singleton().AsSelf();` line and before the `services.RemoveServiceTypeExact<WatchdogsBackgroundService...>` line:

```csharp
            services.Add<StaleJobWatchdog>().Singleton().AsSelf();

            services.RemoveServiceTypeExact<StaleJobMetricHandler, INotificationHandler<StaleJobMetricsNotification>>()
                .Add<StaleJobMetricHandler>()
                .Singleton()
                .AsSelf()
                .AsService<INotificationHandler<StaleJobMetricsNotification>>();
```

- [ ] **Step 3: Add using statements at the top of the file if not already present**

Check if these usings exist. Add any missing ones:

```csharp
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.StaleJob.Messages;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
```

- [ ] **Step 4: Build the full SqlServer project**

```bash
dotnet build src/Microsoft.Health.Fhir.SqlServer/Microsoft.Health.Fhir.SqlServer.csproj --no-restore -p:TreatWarningsAsErrors=false 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/Microsoft.Health.Fhir.SqlServer/Registration/FhirServerBuilderSqlServerRegistrationExtensions.cs
git commit -m "feat: register StaleJobWatchdog and StaleJobMetricHandler as singletons"
```

---

## Task 6: Integration test

**Files:**
- Modify: `test/Microsoft.Health.Fhir.Shared.Tests.Integration/Persistence/SqlServerWatchdogTests.cs`

- [ ] **Step 1: Add required usings to SqlServerWatchdogTests.cs**

At the top of the file, add any of these not already present:

```csharp
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.StaleJob.Messages;
using Microsoft.Health.TaskManagement;
```

- [ ] **Step 2: Add the integration test method**

Add this test at the bottom of the `SqlServerWatchdogTests` class:

```csharp
        [Fact]
        public async Task StaleJobWatchdog_WhenQueueIsEmpty_PublishesNotificationWithAllQueueTypes()
        {
            var sqlQueueClient = new SqlQueueClient(
                _fixture.SchemaInformation,
                _fixture.SqlRetryService,
                XUnitLogger<SqlQueueClient>.Create(_testOutputHelper));

            var mediator = Substitute.For<IMediator>();
            StaleJobMetricsNotification captured = null;
            mediator.When(x => x.Publish(Arg.Any<StaleJobMetricsNotification>(), Arg.Any<CancellationToken>()))
                    .Do(info => captured = (StaleJobMetricsNotification)info[0]);

            var wd = new StaleJobWatchdog(
                _fixture.SqlRetryService,
                XUnitLogger<StaleJobWatchdog>.Create(_testOutputHelper),
                sqlQueueClient,
                mediator)
            {
                PeriodSec = 1,
                LeasePeriodSec = 2,
                AllowRebalance = true,
            };

            ExecuteSql($"DELETE FROM dbo.Parameters WHERE Id IN ('{wd.PeriodSecId}', '{wd.LeasePeriodSecId}')");
            ExecuteSql($"INSERT INTO dbo.Parameters (Id, Number) VALUES ('{wd.PeriodSecId}', 1)");
            ExecuteSql($"INSERT INTO dbo.Parameters (Id, Number) VALUES ('{wd.LeasePeriodSecId}', 2)");

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            Task wdTask = wd.ExecuteAsync(cts.Token);

            var startTime = DateTime.UtcNow;
            while (captured == null && (DateTime.UtcNow - startTime).TotalSeconds < 20)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);
            }

            await cts.CancelAsync();
            await wdTask;

            Assert.NotNull(captured);
            foreach (var queueType in Enum.GetValues<QueueType>().Where(q => q != QueueType.Unknown))
            {
                Assert.True(captured.QueueAges.ContainsKey(queueType), $"Missing queue type {queueType}");
                Assert.True(captured.QueueAges[queueType] >= 0, $"Negative age for {queueType}");
            }
        }
```

- [ ] **Step 3: Build the integration test project**

```bash
dotnet build test/Microsoft.Health.Fhir.R4.Tests.Integration/ --no-restore -p:TreatWarningsAsErrors=false 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 4: Run the integration test**

```bash
dotnet test test/Microsoft.Health.Fhir.R4.Tests.Integration/ \
  --filter "FullyQualifiedName~StaleJobWatchdog_WhenQueueIsEmpty_PublishesNotificationWithAllQueueTypes" \
  2>&1 | tail -15
```

Expected: 1 test passed.

- [ ] **Step 5: Commit**

```bash
git add test/Microsoft.Health.Fhir.Shared.Tests.Integration/Persistence/SqlServerWatchdogTests.cs
git commit -m "test: add StaleJobWatchdog integration test"
```
