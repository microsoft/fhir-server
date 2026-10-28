// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics.Models;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics
{
    /// <summary>
    /// Collects Azure SQL Query Store and statistics diagnostics on a timer and emits them as structured logs.
    /// </summary>
    internal sealed class QueryStoreDiagnosticsWatchdog : Watchdog<QueryStoreDiagnosticsWatchdog>
    {
        internal const int MaxFieldLength = 32 * 1024;

        /// <summary>
        /// The configuration key that sets the collection period, named in the warnings that report the period
        /// being unusable or clamped so the remedy does not have to be looked up.
        /// </summary>
        internal const string PeriodSecConfigurationKey = "FhirServer:Watchdog:QueryStoreDiagnostics:PeriodSec";

        /// <summary>
        /// The configuration key that sets the lease renewal interval, named in the warning that reports the lease
        /// period being unusable so the remedy does not have to be looked up.
        /// </summary>
        internal const string LeasePeriodSecConfigurationKey = "FhirServer:Watchdog:QueryStoreDiagnostics:LeasePeriodSec";

        /// <summary>Wait statistics were read for the plan.</summary>
        internal const string WaitStatisticsAvailableStatus = "Available";

        /// <summary>The wait query succeeded but returned no row for the plan.</summary>
        internal const string WaitStatisticsUnavailableStatus = "Unavailable";

        /// <summary>The wait query itself failed, so wait fields are missing because collection is broken.</summary>
        internal const string WaitStatisticsFailedStatus = "Failed";

        /// <summary>The collection interval used when configuration does not supply a usable one.</summary>
        private const double DefaultPeriodSec = 3600;

        /// <summary>The lease renewal interval used when configuration does not supply a usable one.</summary>
        private const double DefaultLeasePeriodSec = 600;

        private const double MinTimerPeriodSec = 0.001;
        private const double MaxTimerPeriodSec = 4294967.294;

        /// <summary>
        /// The statistics-health batch size used when configuration does not supply a usable one. Mirrors the class
        /// default on <see cref="QueryStoreDiagnosticsConfiguration.StatisticsHealthBatchSize"/>.
        /// </summary>
        private const int DefaultStatisticsHealthBatchSize = 20;

        /// <summary>
        /// The largest number of statistics rows packed into one log line. This limits row count, not byte size.
        /// </summary>
        private const int MaxStatisticsHealthBatchSize = 64;

        /// <summary>The shortest lookback window a collection is allowed to use.</summary>
        private const double MinLookbackPeriodSec = 60;

        /// <summary>The longest lookback window a collection is allowed to use.</summary>
        private const double MaxLookbackPeriodSec = 86400;

        private readonly QueryStoreDiagnosticsConfiguration _configuration;
        private readonly ILogger<QueryStoreDiagnosticsWatchdog> _logger;
        private readonly ISqlRetryService _sqlRetryService;
        private readonly SchemaInformation _schemaInformation;

        // The run-window state observed on the previous tick, used to log only when the state changes. Null until the
        // first observation, so that the state a process starts in is always reported once and an operator never has
        // to infer "the window has not opened yet" from the absence of collection logs. Ticks of a given watchdog
        // instance are sequential — FhirTimer awaits each RunWorkAsync before the next tick — so this needs no
        // synchronization.
        private RunWindowState? _lastRunWindowState;

        public QueryStoreDiagnosticsWatchdog(
            ISqlRetryService sqlRetryService,
            ILogger<QueryStoreDiagnosticsWatchdog> logger,
            IOptions<WatchdogConfiguration> watchdogConfiguration,
            SchemaInformation schemaInformation)
            : base(sqlRetryService, logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _configuration = EnsureArg.IsNotNull(watchdogConfiguration?.Value, nameof(watchdogConfiguration)).QueryStoreDiagnostics;
            _schemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));

            // PeriodicTimer accepts periods from one millisecond through 4,294,967,294 milliseconds.
            if (_configuration.PeriodSec >= MinTimerPeriodSec && _configuration.PeriodSec <= MaxTimerPeriodSec)
            {
                PeriodSec = _configuration.PeriodSec;
            }
            else
            {
                PeriodSec = DefaultPeriodSec;
                _logger.LogWarning(
                    "QueryStoreDiagnosticsWatchdog: configured PeriodSec is {ConfiguredPeriodSec}, which is not a usable collection interval. Falling back to {FallbackPeriodSec} seconds. Configure '{PeriodSecConfigurationKey}' between {MinTimerPeriodSec} and {MaxTimerPeriodSec} seconds.",
                    _configuration.PeriodSec,
                    DefaultPeriodSec,
                    PeriodSecConfigurationKey,
                    MinTimerPeriodSec,
                    MaxTimerPeriodSec);
            }

            if (_configuration.LeasePeriodSec >= MinTimerPeriodSec && _configuration.LeasePeriodSec <= MaxTimerPeriodSec)
            {
                LeasePeriodSec = _configuration.LeasePeriodSec;
            }
            else
            {
                LeasePeriodSec = DefaultLeasePeriodSec;
                _logger.LogWarning(
                    "QueryStoreDiagnosticsWatchdog: configured LeasePeriodSec is {ConfiguredLeasePeriodSec}, which is not a usable lease renewal interval. Falling back to {FallbackLeasePeriodSec} seconds. Configure '{LeasePeriodSecConfigurationKey}' between {MinTimerPeriodSec} and {MaxTimerPeriodSec} seconds.",
                    _configuration.LeasePeriodSec,
                    DefaultLeasePeriodSec,
                    LeasePeriodSecConfigurationKey,
                    MinTimerPeriodSec,
                    MaxTimerPeriodSec);
            }

            AllowDbPeriodOverride = false;
            if (_configuration.Enabled)
            {
                ReportConfiguredRunWindow();
            }
        }

        /// <summary>
        /// Where the current time sits relative to the configured run window. Tracked between ticks so that the
        /// skip is reported when it starts and stops rather than on every tick: at the default hourly period, a
        /// window that opens in a month would otherwise produce around 720 identical lines before collecting anything.
        /// </summary>
        private enum RunWindowState
        {
            /// <summary>The window has a start and the current time has not reached it yet.</summary>
            BeforeWindow,

            /// <summary>The current time is inside the window, so collection proceeds.</summary>
            InWindow,

            /// <summary>The window has an end and the current time has reached or passed it.</summary>
            AfterWindow,
        }

        /// <summary>
        /// Gets or sets the interval, in seconds, between collections.
        /// </summary>
        public override double PeriodSec { get; internal set; }

        /// <summary>
        /// Gets or sets the lease renewal interval, in seconds.
        /// </summary>
        public override double LeasePeriodSec { get; internal set; }

        /// <summary>
        /// Gets or sets a value indicating whether the lease may be handed to another replica to balance watchdogs
        /// across a deployment. Matches what every other watchdog asks for.
        /// </summary>
        public override bool AllowRebalance { get; internal set; } = true;

        /// <summary>
        /// Exposes RunWorkAsync for unit testing purposes.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        internal Task RunWorkForTestingAsync(CancellationToken cancellationToken) => RunWorkAsync(cancellationToken);

        /// <summary>
        /// Reports the configured run window at startup: a window that can never open as a warning, and any
        /// configured window as its effective UTC bounds. Exposed as internal only for unit testing.
        /// </summary>
        internal void ReportConfiguredRunWindow()
        {
            DateTimeOffset? runStartDate = _configuration.RunStartDate;
            DateTimeOffset? runEndDate = _configuration.RunEndDate;

            if (runStartDate.HasValue && runEndDate.HasValue && runStartDate.Value >= runEndDate.Value)
            {
                // The end bound is exclusive, so a start equal to the end is as empty as a start after it, and neither
                // is reachable by any clock value. Nothing downstream will ever complain, so this is the only place
                // the mistake can surface.
                _logger.LogWarning(
                    "QueryStoreDiagnosticsWatchdog: the configured run window is empty because RunStartDate {RunStartDate} is not before RunEndDate {RunEndDate}, so no diagnostics will ever be collected. Set RunStartDate earlier than RunEndDate, or clear one of them.",
                    runStartDate.Value,
                    runEndDate.Value);
            }

            if (runStartDate.HasValue || runEndDate.HasValue)
            {
                // Echoed in UTC on purpose: a value configured without an explicit offset is bound in the host's local
                // timezone, and the configured text looks identical either way. Resolving it to a UTC instant here
                // lets an operator who meant UTC catch the mistake immediately, rather than discovering it when the
                // window silently opens hours late or, for a short window, not visibly at all.
                _logger.LogInformation(
                    "QueryStoreDiagnosticsWatchdog: diagnostics collection is limited to a run window. In UTC it starts at {RunStartDateUtc} inclusive and ends at {RunEndDateUtc} exclusive, where an unset bound is unbounded. Current UTC time is {UtcNow}.",
                    runStartDate?.ToUniversalTime(),
                    runEndDate?.ToUniversalTime(),
                    DateTimeOffset.UtcNow);
            }
        }

        /// <summary>
        /// Clamps the configured collection period into the supported lookback range, reporting what the clamp costs
        /// when it changes the value. Takes the period as a parameter rather than reading
        /// <see cref="PeriodSec"/> directly so that both clamp directions are reachable from a unit test.
        /// Exposed as internal only for unit testing.
        /// </summary>
        /// <param name="configuredPeriodSec">The collection period this instance is running at.</param>
        /// <returns>The lookback window, in seconds, to use for this collection.</returns>
        internal double GetLookbackPeriodSec(double configuredPeriodSec)
        {
            var lookbackPeriodSec = Math.Clamp(configuredPeriodSec, MinLookbackPeriodSec, MaxLookbackPeriodSec);

            // The tick interval is the configured period unclamped, so whenever the clamp bites the interval and the
            // window it covers decouple permanently and silently — every tick after the first inherits the same gap
            // or the same overlap.
            if (lookbackPeriodSec < configuredPeriodSec)
            {
                _logger.LogWarning(
                    "QueryStoreDiagnosticsWatchdog: the configured PeriodSec of {ConfiguredPeriodSec} seconds exceeds the maximum lookback window, so every collection looks back only {LookbackPeriodSec} seconds and {UnexaminedPeriodSec} seconds of each interval are never examined. Set '{PeriodSecConfigurationKey}' to at most {MaxLookbackPeriodSec} seconds.",
                    configuredPeriodSec,
                    lookbackPeriodSec,
                    configuredPeriodSec - lookbackPeriodSec,
                    PeriodSecConfigurationKey,
                    MaxLookbackPeriodSec);
            }
            else if (lookbackPeriodSec > configuredPeriodSec)
            {
                _logger.LogWarning(
                    "QueryStoreDiagnosticsWatchdog: the configured PeriodSec of {ConfiguredPeriodSec} seconds is below the minimum lookback window, so every collection looks back {LookbackPeriodSec} seconds and consecutive collections overlap and re-report the same plans. Set '{PeriodSecConfigurationKey}' to at least {MinLookbackPeriodSec} seconds.",
                    configuredPeriodSec,
                    lookbackPeriodSec,
                    PeriodSecConfigurationKey,
                    MinLookbackPeriodSec);
            }

            return lookbackPeriodSec;
        }

        /// <summary>
        /// Determines whether the supplied instant falls inside the configured run window, reporting the state only
        /// when it changes. The instant is a parameter rather than read from the clock inside, for the same reason
        /// <see cref="GetLookbackPeriodSec(double)"/> takes the stored period as one: it is the only way to reach
        /// every branch from a unit test without introducing a mockable clock abstraction for a single comparison.
        /// Exposed as internal only for unit testing.
        /// </summary>
        /// <param name="utcNow">The current time, supplied by the caller.</param>
        /// <returns>True when collection should proceed; false when the window has not opened or has closed.</returns>
        internal bool IsWithinRunWindow(DateTimeOffset utcNow)
        {
            DateTimeOffset? runStartDate = _configuration.RunStartDate;
            DateTimeOffset? runEndDate = _configuration.RunEndDate;

            // DateTimeOffset comparisons are made on the underlying UTC instant, so a bound configured with any offset
            // — including one implied by the host's local timezone — compares correctly against a UTC clock reading
            // without converting anything first.
            RunWindowState state;
            if (runStartDate.HasValue && utcNow < runStartDate.Value)
            {
                state = RunWindowState.BeforeWindow;
            }
            else if (runEndDate.HasValue && utcNow >= runEndDate.Value)
            {
                // The end bound is exclusive, so the instant that equals it is already outside. That also makes
                // adjacent windows tile without overlapping.
                state = RunWindowState.AfterWindow;
            }
            else
            {
                state = RunWindowState.InWindow;
            }

            if (_lastRunWindowState != state)
            {
                _lastRunWindowState = state;
                LogRunWindowState(state, runStartDate, runEndDate, utcNow);
            }

            return state == RunWindowState.InWindow;
        }

        /// <summary>
        /// Emits the one line that explains why collection is or is not happening, on the tick where the run-window
        /// state changed.
        /// </summary>
        /// <param name="state">The state that has just been entered.</param>
        /// <param name="runStartDate">The configured start bound, or null when unbounded.</param>
        /// <param name="runEndDate">The configured end bound, or null when unbounded.</param>
        /// <param name="utcNow">The instant the state was observed at.</param>
        private void LogRunWindowState(RunWindowState state, DateTimeOffset? runStartDate, DateTimeOffset? runEndDate, DateTimeOffset utcNow)
        {
            // Information rather than warning throughout: a closed window is the feature working as configured, not a
            // misconfiguration. Bounds are echoed in UTC so they can be compared against the current time at a glance
            // without mentally applying whatever offset they were configured with.
            switch (state)
            {
                case RunWindowState.BeforeWindow:
                    _logger.LogInformation(
                        "QueryStoreDiagnosticsWatchdog: the configured run window has not opened yet, so collection is skipped. It opens at {RunStartDateUtc} and the current UTC time is {UtcNow}.",
                        runStartDate?.ToUniversalTime(),
                        utcNow);
                    break;

                case RunWindowState.AfterWindow:
                    // Says explicitly that ticking continues, because the obvious reading of "the window has closed"
                    // is that the watchdog stopped, and someone would otherwise go looking for a process that died.
                    _logger.LogInformation(
                        "QueryStoreDiagnosticsWatchdog: the configured run window closed at {RunEndDateUtc} and the current UTC time is {UtcNow}, so collection is skipped. The watchdog keeps ticking and will collect again if the window is widened and the host restarted.",
                        runEndDate?.ToUniversalTime(),
                        utcNow);
                    break;

                default:
                    _logger.LogInformation(
                        "QueryStoreDiagnosticsWatchdog: collection is within the configured run window, which starts at {RunStartDateUtc} inclusive and ends at {RunEndDateUtc} exclusive in UTC, where an unset bound is unbounded. The current UTC time is {UtcNow}.",
                        runStartDate?.ToUniversalTime(),
                        runEndDate?.ToUniversalTime(),
                        utcNow);
                    break;
            }
        }

        protected override async Task RunWorkAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!_configuration.Enabled)
                {
                    // Unreachable in the host as it stands: WatchdogsBackgroundService reads the same configuration
                    // snapshot and never starts this watchdog when the feature is off, and IOptions<T> does not
                    // reload in place. It is kept because the watchdog is registered AsSelf and this is the only
                    // place the opt-out is enforced at the unit of work — any future call site that executes a
                    // collection directly would otherwise read Query Store on a deployment that opted out. One bool
                    // read per period is not a cost worth trading that away for.
                    _logger.LogInformation("QueryStoreDiagnosticsWatchdog is disabled by configuration. Exiting...");
                    return;
                }

                // One clock reading serves both the window decision and the collection window, so a tick that is
                // admitted cannot then collect for an instant on the other side of its own boundary.
                var collectionTime = DateTimeOffset.UtcNow;
                if (!IsWithinRunWindow(collectionTime))
                {
                    // Deliberately a skipped tick rather than a shutdown, even once the end date has passed. Stopping
                    // would mean faulting or completing this watchdog's task, and WatchdogsBackgroundService cancels
                    // the token shared by EVERY watchdog as soon as one task completes — so self-terminating an
                    // off-by-default diagnostics feature would take the transaction and cleanup watchdogs down with
                    // it. PeriodSec and timer faults propagate the same way for the same reason. A clock comparison
                    // once an hour costs nothing; the alternative costs the host.
                    return;
                }

                if (!_schemaInformation.Current.HasValue || _schemaInformation.Current.Value < SchemaVersionConstants.QueryStoreDiagnosticsVersion)
                {
                    _logger.LogWarning(
                        "QueryStoreDiagnosticsWatchdog: diagnostics require schema version {RequiredSchemaVersion} or later; the current version is {CurrentSchemaVersion}. Collection will retry on the next tick after the schema is available.",
                        SchemaVersionConstants.QueryStoreDiagnosticsVersion,
                        _schemaInformation.Current);
                    return;
                }

                var lookbackPeriodSec = GetLookbackPeriodSec(PeriodSec);
                var startTime = collectionTime.AddSeconds(-lookbackPeriodSec);
                var queryStoreState = await GetQueryStoreStateAsync(cancellationToken);
                if (queryStoreState == null)
                {
                    _logger.LogWarning(
                        "QueryStoreDiagnosticsWatchdog: sys.database_query_store_options returned no row, so Query Store is not configured on this database. Skipping collection.");
                    return;
                }

                if (!string.Equals(queryStoreState.ActualState, "READ_WRITE", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "QueryStoreDiagnosticsWatchdog: Query Store is unavailable for diagnostics. State={QueryStoreState}, ReadonlyReason={ReadonlyReason}, ReadonlyReasonDescription={ReadonlyReasonDescription}",
                        queryStoreState.ActualState,
                        queryStoreState.ReadonlyReason,
                        DescribeReadonlyReason(queryStoreState.ReadonlyReason));
                    return;
                }

                await CollectDiagnosticsAsync(startTime, collectionTime, cancellationToken);
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                // This filter covers the whole method body rather than each read, which loses the ability to name the
                // failing statement. That is the accepted trade-off: the watchdog only runs when an operator enabled
                // it in configuration, so "the views you asked me to read do not exist" is always operator-actionable
                // and permanent, and per-read catches would be more churn than value.
                // Because the filter spans every read, the missing view can be the last one, after slow queries and
                // plans have already been emitted; the message is therefore deliberately worded to be true of a
                // partial tick as well as of one that emitted nothing.
                _logger.LogWarning(ex, "QueryStoreDiagnosticsWatchdog: collection was aborted because a required Query Store or statistics view is unavailable. Any diagnostics already emitted during this collection were still logged.");
            }
            catch (SqlException ex) when (ex.Number == 2812)
            {
                _logger.LogWarning(ex, "QueryStoreDiagnosticsWatchdog: a required diagnostics stored procedure is missing. Verify that schema version {RequiredSchemaVersion} is fully installed. Any diagnostics already emitted during this collection were still logged.", SchemaVersionConstants.QueryStoreDiagnosticsVersion);
            }
            catch (SqlException ex) when (ex.Number == 229 || ex.Number == 262 || ex.Number == 297 || ex.Number == 300)
            {
                _logger.LogWarning(ex, "QueryStoreDiagnosticsWatchdog: SQL permissions do not allow diagnostics collection.");
            }
        }

        /// <summary>
        /// Runs the collection itself, once the configuration, run-window and Query Store state gates have all
        /// passed. Separated from <see cref="RunWorkAsync"/> so that the reads and the log lines they produce
        /// are reachable from unit tests, and so that exception handling stays in one place in the caller.
        /// </summary>
        /// <param name="startTime">The start of the collection window.</param>
        /// <param name="collectionTime">The end of the collection window.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        internal async Task CollectDiagnosticsAsync(DateTimeOffset startTime, DateTimeOffset collectionTime, CancellationToken cancellationToken)
        {
            IReadOnlyList<SlowQueryResult> slowQueries = Array.Empty<SlowQueryResult>();
            var waitStatisticsFailed = false;
            if (_configuration.SlowQueryCount <= 0)
            {
                // Skipping the round-trip rather than running it with TOP (0) keeps this degenerate configuration
                // reading the same way as the StatisticsHealthCount one below.
                _logger.LogWarning(
                    "QueryStoreDiagnosticsWatchdog: SlowQueryCount is {SlowQueryCount}, which disables slow-query collection. Configure a positive value to collect slow queries.",
                    _configuration.SlowQueryCount);
            }
            else
            {
                slowQueries = await GetSlowQueriesAsync(startTime, cancellationToken);
                var waitStatistics = await GetWaitStatisticsAsync(startTime, slowQueries, cancellationToken);
                waitStatisticsFailed = waitStatistics.Failed;

                foreach (var slowQuery in slowQueries)
                {
                    waitStatistics.Waits.TryGetValue(slowQuery.PlanId, out var wait);
                    var queryText = slowQuery.QueryText;
                    var queryTextTruncated = queryText.Length > MaxFieldLength;
                    LogSlowQuery(
                        new SlowQueryDiagnostics
                        {
                            QueryId = slowQuery.QueryId,
                            PlanId = slowQuery.PlanId,
                            ExecutionCount = slowQuery.ExecutionCount,
                            TotalDurationMilliseconds = slowQuery.TotalDurationMilliseconds,
                            AverageDurationMilliseconds = slowQuery.AverageDurationMilliseconds,
                            MaxDurationMilliseconds = slowQuery.MaxDurationMilliseconds,
                            TotalCpuMilliseconds = slowQuery.TotalCpuMilliseconds,
                            AverageCpuMilliseconds = slowQuery.AverageCpuMilliseconds,
                            TotalLogicalReads = slowQuery.TotalLogicalReads,
                            AverageLogicalReads = slowQuery.AverageLogicalReads,
                            TotalWaitMilliseconds = wait?.TotalWaitMilliseconds,
                            AverageWaitMilliseconds = wait == null ? null : wait.TotalWaitMilliseconds / slowQuery.ExecutionCount,
                            TopWaitCategory = wait?.TopWaitCategory,
                            WaitStatisticsStatus = GetWaitStatisticsStatus(waitStatistics.Failed, wait),
                            QueryText = queryTextTruncated ? queryText.Substring(0, MaxFieldLength) : queryText,
                            QueryTextTruncated = queryTextTruncated,
                            QueryTextLength = queryText.Length,
                            IntervalStart = slowQuery.IntervalStart,
                            IntervalEnd = slowQuery.IntervalEnd,
                        });
                }
            }

            var queryPlanCount = 0;
            if (!_configuration.IncludeQueryPlans)
            {
                _logger.LogInformation("QueryStoreDiagnosticsWatchdog: query plan collection is turned off by configuration (IncludeQueryPlans).");
            }
            else if (slowQueries.Count > 0)
            {
                queryPlanCount = await EmitQueryPlansAsync(slowQueries, cancellationToken);
            }

            var statisticsHealthCount = 0;
            if (!_configuration.IncludeStatisticsHealth)
            {
                _logger.LogInformation("QueryStoreDiagnosticsWatchdog: statistics health collection is turned off by configuration (IncludeStatisticsHealth).");
            }
            else if (_configuration.StatisticsHealthCount <= 0)
            {
                _logger.LogWarning(
                    "QueryStoreDiagnosticsWatchdog: StatisticsHealthCount is {StatisticsHealthCount}, which disables statistics health collection. Configure a positive value to collect statistics health.",
                    _configuration.StatisticsHealthCount);
            }
            else
            {
                statisticsHealthCount = await EmitStatisticsHealthAsync(cancellationToken);
            }

            // A completed tick logs unconditionally, including zero counts: without this, "the watchdog has been
            // dead for three days" and "there were no slow queries" are indistinguishable downstream. QueryPlans
            // counts the plans that actually carried sanitized XML, so it is deliberately lower than SlowQueries
            // whenever Query Store had no plan for a query or sanitization rejected one.
            _logger.LogInformation(
                "QueryStoreDiagnosticsWatchdog completed a collection. WindowStart={WindowStart}, WindowEnd={WindowEnd}, SlowQueries={SlowQueryCount}, QueryPlans={QueryPlanCount}, StatisticsHealth={StatisticsHealthCount}, WaitStatisticsFailed={WaitStatisticsFailed}",
                startTime,
                collectionTime,
                slowQueries.Count,
                queryPlanCount,
                statisticsHealthCount,
                waitStatisticsFailed);
        }

        private async Task<QueryStoreState> GetQueryStoreStateAsync(CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand("dbo.GetQueryStoreDiagnosticsState") { CommandType = CommandType.StoredProcedure };

            // Every diagnostics read binds to the primary: isReadOnly would route to a read-only secondary when
            // SupportsSqlReplicas is on, and Query Store state is primary-scoped, so a secondary reports READ_ONLY
            // and the state gate below would silently disable collection forever. Replica routing is also decided
            // per call, so the state check and the data reads could otherwise land on different servers and produce
            // torn results.
            var states = await _sqlRetryService.ExecuteReaderAsync(
                command,
                reader => new QueryStoreState(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1)),
                _logger,
                "Failed to read Query Store state",
                cancellationToken);

            return states.Count == 0 ? null : states[0];
        }

        private async Task<IReadOnlyList<SlowQueryResult>> GetSlowQueriesAsync(DateTimeOffset startTime, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand("dbo.GetQueryStoreDiagnosticsSlowQueries") { CommandType = CommandType.StoredProcedure };
            var minDurationMilliseconds = Math.Max(0, _configuration.MinDurationMilliseconds);
            if (_configuration.MinDurationMilliseconds < 0)
            {
                // Clamping to zero inverts the operator's intent — every query in the window becomes "slow" — so it
                // is reported rather than absorbed, as its sibling thresholds already are.
                _logger.LogWarning(
                    "QueryStoreDiagnosticsWatchdog: MinDurationMilliseconds is {ConfiguredMinDurationMilliseconds}, which is negative and is being treated as {EffectiveMinDurationMilliseconds}, so every query in the collection window qualifies as slow. Configure a non-negative value.",
                    _configuration.MinDurationMilliseconds,
                    minDurationMilliseconds);
            }

            command.Parameters.Add("@StartTime", SqlDbType.DateTimeOffset).Value = startTime;
            command.Parameters.Add("@Top", SqlDbType.Int).Value = Math.Max(0, _configuration.SlowQueryCount);
            command.Parameters.Add("@MinDurationMilliseconds", SqlDbType.Int).Value = minDurationMilliseconds;

            return await _sqlRetryService.ExecuteReaderAsync(
                command,
                reader => new SlowQueryResult
                {
                    QueryId = reader.GetInt64(0),
                    PlanId = reader.GetInt64(1),
                    ExecutionCount = reader.GetInt64(2),
                    TotalDurationMilliseconds = reader.GetDouble(3),
                    AverageDurationMilliseconds = reader.GetDouble(4),
                    MaxDurationMilliseconds = reader.GetDouble(5),
                    TotalCpuMilliseconds = reader.GetDouble(6),
                    AverageCpuMilliseconds = reader.GetDouble(7),
                    TotalLogicalReads = reader.GetDouble(8),
                    AverageLogicalReads = reader.GetDouble(9),
                    QueryText = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                    IntervalStart = reader.GetDateTimeOffset(11),
                    IntervalEnd = reader.GetDateTimeOffset(12),
                },
                _logger,
                "Failed to read Query Store slow queries",
                cancellationToken);
        }

        private async Task<(Dictionary<long, WaitStatistics> Waits, bool Failed)> GetWaitStatisticsAsync(
            DateTimeOffset startTime,
            IReadOnlyList<SlowQueryResult> slowQueries,
            CancellationToken cancellationToken)
        {
            if (slowQueries.Count == 0)
            {
                return (new Dictionary<long, WaitStatistics>(), false);
            }

            try
            {
                await using var command = new SqlCommand("dbo.GetQueryStoreDiagnosticsWaitStatistics") { CommandType = CommandType.StoredProcedure };
                command.Parameters.Add("@StartTime", SqlDbType.DateTimeOffset).Value = startTime;
                command.Parameters.Add("@PlanIds", SqlDbType.NVarChar, -1).Value = string.Join(',', slowQueries.Select(query => query.PlanId));

                var waits = await _sqlRetryService.ExecuteReaderAsync(
                    command,
                    reader => new WaitStatistics(
                        reader.GetInt64(0),
                        reader.GetDouble(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2)),
                    _logger,
                    "Failed to read Query Store wait statistics",
                    cancellationToken);

                return (waits.ToDictionary(wait => wait.PlanId), false);
            }
            catch (SqlException ex) when (ex.Number == 2812)
            {
                _logger.LogWarning(ex, "QueryStoreDiagnosticsWatchdog: the wait statistics stored procedure is missing. Verify that schema version {RequiredSchemaVersion} is fully installed. Wait fields will be empty.", SchemaVersionConstants.QueryStoreDiagnosticsVersion);
                return (new Dictionary<long, WaitStatistics>(), true);
            }
            catch (SqlException ex)
            {
                // SqlException is caught broadly on purpose: a transient wait-query failure must never abort the tick
                // and suppress the runtime statistics, which are the primary signal. The cost of that breadth is
                // that timeouts, deadlocks, permission denials and missing views all look alike here, so the failure
                // is logged as a warning and surfaced on every slow-query line as WaitStatisticsStatus = Failed.
                _logger.LogWarning(ex, "QueryStoreDiagnosticsWatchdog: Query Store wait statistics could not be read for this collection. Wait fields will be empty.");
                return (new Dictionary<long, WaitStatistics>(), true);
            }
        }

        private async Task<int> EmitQueryPlansAsync(IReadOnlyList<SlowQueryResult> slowQueries, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand("dbo.GetQueryStoreDiagnosticsPlans") { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@PlanIds", SqlDbType.NVarChar, -1).Value = string.Join(',', slowQueries.Select(query => query.PlanId));

            var plans = await _sqlRetryService.ExecuteReaderAsync(
                command,
                reader => (PlanId: reader.GetInt64(0), QueryPlanXml: reader.IsDBNull(1) ? null : reader.GetString(1)),
                _logger,
                "Failed to read Query Store plans",
                cancellationToken);

            // Keyed by plan id and holding the plan XML itself: a plan with no row and a plan whose row carries NULL
            // XML both yield null here, which is exactly what the sanitizer reports as PlanXmlUnavailable.
            var plansById = plans.ToDictionary(plan => plan.PlanId, plan => plan.QueryPlanXml);
            var emittedPlanCount = 0;

            foreach (var slowQuery in slowQueries)
            {
                plansById.TryGetValue(slowQuery.PlanId, out var queryPlan);
                var sanitizedPlan = QueryPlanSanitizer.Sanitize(queryPlan, MaxFieldLength);
                if (!string.Equals(sanitizedPlan.Status, QueryPlanSanitizer.SanitizedStatus, StringComparison.Ordinal))
                {
                    // Without this, systematic sanitizer breakage looks exactly like "plans are simply unavailable"
                    // unless whoever is reading the plan lines happens to look at SanitizationStatus.
                    _logger.LogWarning(
                        "QueryStoreDiagnosticsWatchdog: query plan was not emitted because sanitization did not succeed. PlanId={PlanId}, SanitizationStatus={SanitizationStatus}",
                        slowQuery.PlanId,
                        sanitizedPlan.Status);
                }

                LogQueryPlan(
                    new QueryPlanDiagnostics
                    {
                        QueryId = slowQuery.QueryId,
                        PlanId = slowQuery.PlanId,
                        SanitizedQueryPlan = sanitizedPlan.Xml,
                        QueryPlanTruncated = sanitizedPlan.Truncated,
                        OriginalQueryPlanLength = sanitizedPlan.OriginalLength,
                        SanitizedQueryPlanLength = sanitizedPlan.SanitizedLength,
                        SanitizationStatus = sanitizedPlan.Status,
                    });

                // A line is emitted for every slow query, including the ones with no usable plan, but only the ones
                // that carried XML are counted: a count that always equalled the slow-query count would tell an
                // operator nothing about whether plans are actually arriving.
                if (sanitizedPlan.Xml != null)
                {
                    emittedPlanCount++;
                }
            }

            return emittedPlanCount;
        }

        private async Task<int> EmitStatisticsHealthAsync(CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand("dbo.GetQueryStoreDiagnosticsStatisticsHealth") { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@Top", SqlDbType.Int).Value = _configuration.StatisticsHealthCount;

            var statisticsHealth = await _sqlRetryService.ExecuteReaderAsync(
                command,
                reader => new StatisticsHealthDiagnostics
                {
                    SchemaName = reader.GetString(0),
                    TableName = reader.GetString(1),
                    StatisticsName = reader.GetString(2),
                    LastUpdated = reader.IsDBNull(3) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(3),
                    Rows = reader.IsDBNull(4) ? (long?)null : reader.GetInt64(4),
                    RowsSampled = reader.IsDBNull(5) ? (long?)null : reader.GetInt64(5),
                    ModificationCounter = reader.IsDBNull(6) ? (long?)null : reader.GetInt64(6),
                    ModificationPercent = reader.IsDBNull(7) ? (double?)null : reader.GetDouble(7),
                    IsAutoCreated = reader.GetBoolean(8),
                    IsUserCreated = reader.GetBoolean(9),
                    IsFromIndex = reader.GetBoolean(10),
                    HasFilter = reader.GetBoolean(11),
                },
                _logger,
                "Failed to read statistics health",
                cancellationToken);

            LogStatisticsHealthBatches(statisticsHealth);

            return statisticsHealth.Count;
        }

        /// <summary>
        /// Emits the collected statistics rows as JSON batches, one log line per page.
        /// </summary>
        /// <param name="statisticsHealth">The collected rows, already capped at StatisticsHealthCount.</param>
        internal void LogStatisticsHealthBatches(IReadOnlyList<StatisticsHealthDiagnostics> statisticsHealth)
        {
            if (statisticsHealth.Count == 0)
            {
                // No line at all rather than an empty page: the collection summary already reports a count of zero,
                // and a page carrying nothing would only make the emitted pages harder to count.
                return;
            }

            var batchSize = _configuration.StatisticsHealthBatchSize;
            if (batchSize <= 0)
            {
                // Unlike the counts, this one cannot degrade to collecting nothing — the rows have already been read
                // and dropping them would lose diagnostics an operator asked for — so it falls back to the default
                // and names the rejected value, the way an unusable PeriodSec does.
                _logger.LogWarning(
                    "QueryStoreDiagnosticsWatchdog: StatisticsHealthBatchSize is {ConfiguredStatisticsHealthBatchSize}, which cannot pack a row. Falling back to {FallbackStatisticsHealthBatchSize} rows per log line. Configure a positive value to change the batch size.",
                    batchSize,
                    DefaultStatisticsHealthBatchSize);
                batchSize = DefaultStatisticsHealthBatchSize;
            }
            else if (batchSize > MaxStatisticsHealthBatchSize)
            {
                // Clamped rather than honoured, because the failure this prevents is not a smaller page but a single
                // record large enough for a sink to truncate or reject, which would lose the rows entirely. Paging
                // costs extra lines, and lines are the cheap thing here.
                _logger.LogWarning(
                    "QueryStoreDiagnosticsWatchdog: StatisticsHealthBatchSize is {ConfiguredStatisticsHealthBatchSize}, which risks a log record too large for the sink to accept. Using {MaxStatisticsHealthBatchSize} rows per log line instead. All rows are still emitted, across more pages.",
                    batchSize,
                    MaxStatisticsHealthBatchSize);
                batchSize = MaxStatisticsHealthBatchSize;
            }

            var pageCount = ((statisticsHealth.Count - 1) / batchSize) + 1;
            for (var page = 0; page < pageCount; page++)
            {
                var pageRows = statisticsHealth.Skip(page * batchSize).Take(batchSize).ToList();

                // Page number, page count and total row count travel on every line so that a reader can tell a
                // partial last page from a set that was cut short by a host that died mid-collection.
                _logger.LogInformation(
                    "QueryStoreDiagnosticsWatchdog statistics health. StatisticsHealthPage={StatisticsHealthPage}, StatisticsHealthPageCount={StatisticsHealthPageCount}, StatisticsHealthPageRowCount={StatisticsHealthPageRowCount}, StatisticsHealthRowCount={StatisticsHealthRowCount}, StatisticsHealthRows={StatisticsHealthRows}",
                    page + 1,
                    pageCount,
                    pageRows.Count,
                    statisticsHealth.Count,
                    JsonSerializer.Serialize(pageRows));
            }
        }

        /// <summary>
        /// Emits one slow query as a single structured line. Every field is its own named property rather than a
        /// serialized document, so each one lands as a queryable column downstream; at the default of ten rows a
        /// tick, a line per row costs nothing worth batching for.
        /// </summary>
        /// <param name="slowQuery">The diagnostics to emit.</param>
        private void LogSlowQuery(SlowQueryDiagnostics slowQuery)
        {
            // QueryText is last because it is the one unbounded field on the line, so everything an operator scans
            // for is still readable ahead of it. DiagnosticsTimestamp rather than Timestamp, because that name
            // collides with the ingestion timestamp the log pipeline supplies for every record.
            _logger.LogInformation(
                "QueryStoreDiagnosticsWatchdog slow query. QueryId={QueryId}, PlanId={PlanId}, ExecutionCount={ExecutionCount}, TotalDurationMilliseconds={TotalDurationMilliseconds}, AverageDurationMilliseconds={AverageDurationMilliseconds}, MaxDurationMilliseconds={MaxDurationMilliseconds}, TotalCpuMilliseconds={TotalCpuMilliseconds}, AverageCpuMilliseconds={AverageCpuMilliseconds}, TotalLogicalReads={TotalLogicalReads}, AverageLogicalReads={AverageLogicalReads}, TotalWaitMilliseconds={TotalWaitMilliseconds}, AverageWaitMilliseconds={AverageWaitMilliseconds}, TopWaitCategory={TopWaitCategory}, WaitStatisticsStatus={WaitStatisticsStatus}, QueryTextTruncated={QueryTextTruncated}, QueryTextLength={QueryTextLength}, IntervalStart={IntervalStart}, IntervalEnd={IntervalEnd}, DiagnosticsTimestamp={DiagnosticsTimestamp}, QueryText={QueryText}",
                slowQuery.QueryId,
                slowQuery.PlanId,
                slowQuery.ExecutionCount,
                slowQuery.TotalDurationMilliseconds,
                slowQuery.AverageDurationMilliseconds,
                slowQuery.MaxDurationMilliseconds,
                slowQuery.TotalCpuMilliseconds,
                slowQuery.AverageCpuMilliseconds,
                slowQuery.TotalLogicalReads,
                slowQuery.AverageLogicalReads,
                slowQuery.TotalWaitMilliseconds,
                slowQuery.AverageWaitMilliseconds,
                slowQuery.TopWaitCategory,
                slowQuery.WaitStatisticsStatus,
                slowQuery.QueryTextTruncated,
                slowQuery.QueryTextLength,
                slowQuery.IntervalStart,
                slowQuery.IntervalEnd,
                slowQuery.Timestamp,
                slowQuery.QueryText);
        }

        /// <summary>
        /// Emits one query plan as a single structured line. Not batched with its neighbours: the sanitized XML is
        /// capped at the field length rather than being small, so packing several into one record would risk a
        /// single oversized log row.
        /// </summary>
        /// <param name="queryPlan">The diagnostics to emit.</param>
        private void LogQueryPlan(QueryPlanDiagnostics queryPlan)
        {
            _logger.LogInformation(
                "QueryStoreDiagnosticsWatchdog query plan. QueryId={QueryId}, PlanId={PlanId}, SanitizationStatus={SanitizationStatus}, QueryPlanTruncated={QueryPlanTruncated}, OriginalQueryPlanLength={OriginalQueryPlanLength}, SanitizedQueryPlanLength={SanitizedQueryPlanLength}, DiagnosticsTimestamp={DiagnosticsTimestamp}, SanitizedQueryPlan={SanitizedQueryPlan}",
                queryPlan.QueryId,
                queryPlan.PlanId,
                queryPlan.SanitizationStatus,
                queryPlan.QueryPlanTruncated,
                queryPlan.OriginalQueryPlanLength,
                queryPlan.SanitizedQueryPlanLength,
                queryPlan.Timestamp,
                queryPlan.SanitizedQueryPlan);
        }

        /// <summary>
        /// Decodes the <c>sys.database_query_store_options.readonly_reason</c> bitmask into a readable reason list,
        /// so an operator sees the cause rather than an integer to look up. Exposed as internal only for unit testing.
        /// </summary>
        /// <param name="readonlyReason">The bitmask value, or null when no value was reported.</param>
        /// <returns>A comma-separated description of the set bits.</returns>
        internal static string DescribeReadonlyReason(int? readonlyReason)
        {
            // Every bit this method knows how to name. An unrecognized bit is reported rather than dropped, so a
            // state flag introduced by a newer SQL Server reaches the operator instead of vanishing behind whichever
            // documented bits happened to be set alongside it.
            const int knownReasonMask = 1 | 2 | 4 | 8 | 65536 | 131072;

            if (readonlyReason == null)
            {
                return "not reported";
            }

            if (readonlyReason.Value == 0)
            {
                return "none";
            }

            var reasons = new List<string>();
            if ((readonlyReason.Value & 1) != 0)
            {
                reasons.Add("database is in read-only mode");
            }

            if ((readonlyReason.Value & 2) != 0)
            {
                reasons.Add("database is in single-user mode");
            }

            if ((readonlyReason.Value & 4) != 0)
            {
                reasons.Add("database is in emergency mode");
            }

            if ((readonlyReason.Value & 8) != 0)
            {
                reasons.Add("database is a secondary replica");
            }

            if ((readonlyReason.Value & 65536) != 0)
            {
                reasons.Add("Query Store has reached its size limit (MAX_STORAGE_SIZE_MB)");
            }

            if ((readonlyReason.Value & 131072) != 0)
            {
                reasons.Add("Query Store has reached the limit on the number of statements");
            }

            if (reasons.Count == 0)
            {
                return "unrecognized reason";
            }

            var unrecognizedBits = readonlyReason.Value & ~knownReasonMask;
            if (unrecognizedBits != 0)
            {
                reasons.Add(FormattableString.Invariant($"unrecognized reason bits {unrecognizedBits} (readonly_reason = {readonlyReason.Value})"));
            }

            return string.Join(", ", reasons);
        }

        private static string GetWaitStatisticsStatus(bool waitStatisticsFailed, WaitStatistics wait)
        {
            if (waitStatisticsFailed)
            {
                return WaitStatisticsFailedStatus;
            }

            return wait == null ? WaitStatisticsUnavailableStatus : WaitStatisticsAvailableStatus;
        }

        internal sealed class SlowQueryResult
        {
            internal long QueryId { get; set; }

            internal long PlanId { get; set; }

            internal long ExecutionCount { get; set; }

            internal double TotalDurationMilliseconds { get; set; }

            internal double AverageDurationMilliseconds { get; set; }

            internal double MaxDurationMilliseconds { get; set; }

            internal double TotalCpuMilliseconds { get; set; }

            internal double AverageCpuMilliseconds { get; set; }

            internal double TotalLogicalReads { get; set; }

            internal double AverageLogicalReads { get; set; }

            internal string QueryText { get; set; }

            internal DateTimeOffset IntervalStart { get; set; }

            internal DateTimeOffset IntervalEnd { get; set; }
        }

        private sealed class QueryStoreState
        {
            // Trap: sys.database_query_store_options.readonly_reason is int, NOT bigint, even though the neighbouring
            // Query Store columns this watchdog reads (query_id, plan_id, SUM(count_executions), rows, rows_sampled,
            // modification_counter) genuinely are bigint. Reading it with GetInt64 compiles and only fails at runtime
            // with InvalidCastException, so it must be read with GetInt32 and held as int.
            internal QueryStoreState(string actualState, int? readonlyReason)
            {
                ActualState = actualState;
                ReadonlyReason = readonlyReason;
            }

            internal string ActualState { get; }

            internal int? ReadonlyReason { get; }
        }

        internal sealed class WaitStatistics
        {
            internal WaitStatistics(long planId, double totalWaitMilliseconds, string topWaitCategory)
            {
                PlanId = planId;
                TotalWaitMilliseconds = totalWaitMilliseconds;
                TopWaitCategory = topWaitCategory;
            }

            internal long PlanId { get; }

            internal double TotalWaitMilliseconds { get; }

            internal string TopWaitCategory { get; }
        }
    }
}
