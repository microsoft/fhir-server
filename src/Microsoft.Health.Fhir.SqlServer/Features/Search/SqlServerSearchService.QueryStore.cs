// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    /// <summary>
    /// SQL Server search service implementation.
    /// </summary>
    internal partial class SqlServerSearchService
    {
        internal const string LongRunningQueryDetailsParameterId = "Search.LongRunningQueryDetails.IsEnabled";
        internal const string LongRunningQueryDetailsThresholdId = "Search.LongRunningQueryDetails.Threshold";
        internal const int LongRunningThresholdMillisecondsDefault = 5000;
        private static readonly string[] NewLineSeparators = ["\r\n", "\n"];
        private static readonly Regex WhitespacePattern = new Regex(@"\s+", RegexOptions.Compiled);

        /// <summary>
        /// Hard cap for the diagnostic query command timeout (seconds). The CancellationToken
        /// timeout (2s) is the first line of defense; this is a backup in case cancellation
        /// doesn't terminate the SQL command promptly. Set on a NEW connection — does not
        /// affect search query connections.
        /// </summary>
        internal const int QueryStoreLookupTimeoutSeconds = 5;

        /// <summary>
        /// Maximum number of diagnostic Query Store lookups allowed to run concurrently across the
        /// process. Each lookup opens its own SQL connection, so this caps the diagnostic feature's
        /// connection and CPU footprint. Slots are acquired with a zero-wait try-or-skip, so a burst
        /// of long-running queries can never storm the server with diagnostic lookups. Kept small
        /// because the backing database can be a shared elastic pool, where a large aggregate of
        /// concurrent diagnostic lookups (per pod) would compete with customer traffic.
        /// </summary>
        internal const int MaxConcurrentQueryStoreLookups = 5;
        private static readonly SemaphoreSlim _queryStoreLookupGate = new SemaphoreSlim(MaxConcurrentQueryStoreLookups, MaxConcurrentQueryStoreLookups);

        /// <summary>
        /// Number of consecutive Query Store lookup failures (errors or timeouts) that trips the
        /// diagnostic circuit breaker. Once tripped, Query Store enrichment is suspended for
        /// <see cref="QueryStoreCircuitBreakerCooldown"/> so a truly overloaded database is not
        /// compounded by diagnostic load. Any single successful lookup resets the counter to zero.
        /// </summary>
        internal const int QueryStoreCircuitBreakerFailureThreshold = 5;

        /// <summary>
        /// How long Query Store enrichment stays suspended after the circuit breaker trips. When the
        /// cooldown elapses, exactly one probe lookup is allowed through: if it succeeds the breaker
        /// resets, otherwise the cooldown restarts. Slow-query warnings are always logged regardless
        /// of breaker state — only the Query Store stats lookup is skipped.
        /// </summary>
        internal static readonly TimeSpan QueryStoreCircuitBreakerCooldown = TimeSpan.FromSeconds(10);

        // Circuit breaker state for the diagnostic Query Store lookups. Static (per-process/per-pod)
        // and mutated only through Interlocked/Volatile so no lock is needed on the hot search path.
        // _queryStoreConsecutiveFailures counts consecutive failures; when it reaches the threshold
        // the breaker is "open" until _queryStoreCircuitOpenUntilTicks (a DateTime.UtcNow.Ticks
        // deadline). A value of 0 means the breaker is closed.
        private static int _queryStoreConsecutiveFailures;
        private static long _queryStoreCircuitOpenUntilTicks;

        private static CachedParameter<SqlServerSearchService> _longRunningQueryDetails;
        private static CachedParameter<SqlServerSearchService> _longRunningThreshold;

        /// <summary>
        /// Strips diagnostic preamble lines (SET STATISTICS, DECLARE, OPTION (RECOMPILE), timeout comments)
        /// and normalizes leading ;WITH to WITH, producing a query body suitable for Query Store text matching.
        /// </summary>
        private static string StripQueryPreambleLines(string queryText)
        {
            if (string.IsNullOrWhiteSpace(queryText))
            {
                return string.Empty;
            }

            var lines = queryText.Split(NewLineSeparators, StringSplitOptions.None);
            var sb = new StringBuilder(queryText.Length);
            bool hasContent = false;
            int pendingNewlines = 0;
            foreach (var line in lines)
            {
                ReadOnlySpan<char> trimmed = line.AsSpan().Trim();

                // Track blank lines but defer emitting them
                if (trimmed.IsEmpty)
                {
                    if (hasContent)
                    {
                        pendingNewlines++;
                    }

                    continue;
                }

                // Skip SET STATISTICS lines
                if (trimmed.StartsWith("SET STATISTICS IO", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("SET STATISTICS TIME", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Skip all DECLARE lines
                if (trimmed.StartsWith("DECLARE ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Skip OPTION (RECOMPILE) and execution timeout comments
                if (trimmed.StartsWith("OPTION (RECOMPILE)", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("-- execution timeout", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Emit deferred newlines only when another content line follows
                if (hasContent)
                {
                    // Always emit one newline to separate from previous line,
                    // plus at most one additional blank line if there were blank lines
                    sb.Append("\r\n");
                    if (pendingNewlines > 0)
                    {
                        sb.Append("\r\n");
                    }
                }

                pendingNewlines = 0;

                // Replace ;WITH with WITH using slicing instead of string.Replace
                if (trimmed.StartsWith(";WITH", StringComparison.OrdinalIgnoreCase))
                {
                    int semiPos = line.IndexOf(';', StringComparison.Ordinal);
                    sb.Append(line.AsSpan(0, semiPos));
                    sb.Append(line.AsSpan(semiPos + 1));
                }
                else
                {
                    sb.Append(line);
                }

                hasContent = true;
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Splits a normalized query text into search fragments for Query Store lookup.
        /// Query Store splits multi-statement batches at INSERT INTO @FilteredData boundaries.
        /// </summary>
        internal static List<string> SplitIntoSearchFragments(string normalizedQueryText)
        {
            const string BatchSeparator = "INSERT INTO @FilteredData";
            var searchFragments = new List<string>();

            int separatorIndex = normalizedQueryText.IndexOf(BatchSeparator, StringComparison.OrdinalIgnoreCase);
            if (separatorIndex < 0)
            {
                searchFragments.Add(normalizedQueryText);
            }
            else
            {
                int insertLineEnd = normalizedQueryText.IndexOfAny(['\r', '\n'], separatorIndex);
                if (insertLineEnd < 0)
                {
                    insertLineEnd = normalizedQueryText.Length;
                }

                string firstSegment = normalizedQueryText[..insertLineEnd].Trim();
                if (firstSegment.Length > 0)
                {
                    searchFragments.Add(firstSegment);
                }

                string remainingSegment = normalizedQueryText[insertLineEnd..].Trim();
                if (remainingSegment.Length > 0)
                {
                    searchFragments.Add(remainingSegment);
                }
            }

            return searchFragments;
        }

        /// <summary>
        /// Removes all whitespace characters (tab/CHAR(9), LF/CHAR(10), VT/CHAR(11), FF/CHAR(12),
        /// CR/CHAR(13), space/CHAR(32), and any other Unicode whitespace matched by <c>\s</c>) from the text.
        /// This enables robust whitespace-insensitive comparison between the local query text and what
        /// SQL Server Query Store may store — different database engines or drivers can add or reformat
        /// whitespace in unpredictable ways, so the safest comparison strips all whitespace entirely
        /// rather than trying to collapse or normalise it.
        /// The SQL side mirrors this by stripping CHAR(9)/CHAR(10)/CHAR(11)/CHAR(12)/CHAR(13)/CHAR(32).
        /// </summary>
        internal static string StripAllWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return WhitespacePattern.Replace(text, string.Empty);
        }

        /// <summary>
        /// Strips the <c>dbo.</c> schema prefix from a stored procedure name so it matches
        /// what SQL Server Query Store records in <c>sys.query_store_query_text.query_sql_text</c>.
        /// Query Store stores only the bare procedure name without the schema qualifier.
        /// The comparison is case-insensitive to handle mixed-case schemas such as <c>DBO.</c>.
        /// If the name has no <c>dbo.</c> prefix (or no prefix at all) it is returned unchanged.
        /// </summary>
        internal static string StripDboSchemaPrefix(string procName) =>
            procName?.Replace("dbo.", string.Empty, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Extracts the parameter hash value from a query text that contains a
        /// <c>/* HASH {base64hash} params=... */</c> comment embedded by <see cref="Expressions.Visitors.QueryGenerators.SqlQueryGenerator"/>.
        /// Returns <c>null</c> if no hash comment is found.
        /// </summary>
        internal static string ExtractParameterHash(string queryText)
        {
            if (string.IsNullOrEmpty(queryText))
            {
                return null;
            }

            // ParametersHashStart/End are always emitted in fixed uppercase by SqlQueryGenerator,
            // so use Ordinal (not OrdinalIgnoreCase) to avoid matching arbitrary user-authored
            // lowercase comments such as "/* hash ... */".
            int hashStart = queryText.IndexOf(SqlSearchConstants.ParametersHashStart, StringComparison.Ordinal);
            if (hashStart < 0)
            {
                return null;
            }

            int valueStart = hashStart + SqlSearchConstants.ParametersHashStart.Length;
            int hashEnd = queryText.IndexOf(SqlSearchConstants.ParametersHashEnd, valueStart, StringComparison.Ordinal);
            if (hashEnd < 0)
            {
                return null;
            }

            // Extract just the base64 hash, stopping at the space before "params="
            string hashAndParams = queryText[valueStart..hashEnd];
            int spaceIndex = hashAndParams.IndexOf(' ', StringComparison.Ordinal);
            string hash = spaceIndex >= 0 ? hashAndParams[..spaceIndex] : hashAndParams;

            // Guard against an empty/whitespace-only hash, which would make the downstream
            // LIKE '%/* HASH {hash}%' filter match every hash-bearing row.
            return string.IsNullOrWhiteSpace(hash) ? null : hash;
        }

        /// <summary>
        /// Runs <see cref="LogQueryStoreByTextAsync"/> as a fire-and-forget background task so the
        /// diagnostic Query Store lookup never blocks or fails the originating search request.
        /// </summary>
        private void FireAndForgetQueryStoreLookup(string queryText, bool isStoredProcedure, long executionTime)
        {
            // Circuit breaker: if too many consecutive lookups have failed/timed out, the database is
            // likely overloaded. Suspend Query Store enrichment for a cooldown window so diagnostics
            // don't compound the problem. The slow query is still logged below — only the enrichment
            // is skipped. When the cooldown elapses, the breaker closes and lookups resume (bounded by
            // the concurrency gate below); the failure counter stays elevated, so the next failure
            // re-opens the breaker while any success fully resets it.
            if (!TryEnterQueryStoreCircuit())
            {
                _logger.LogWarning(
                    "Long-running SQL ({ElapsedMilliseconds}ms). Query={Query} QueryStoreStats={QueryStoreStats}",
                    executionTime,
                    queryText,
                    "Skipped: diagnostic circuit breaker open (database appears overloaded).");
                return;
            }

            // Try-or-skip: grab a diagnostic slot without waiting. If all slots are already taken,
            // skip the expensive Query Store enrichment (which opens a new DB connection) rather than
            // queueing it. This prevents a burst of long-running queries from each opening a diagnostic
            // connection and storming the server. We still emit the long-running warning so the slow
            // query is never lost — only the enrichment is dropped.
            if (!_queryStoreLookupGate.Wait(0))
            {
                _logger.LogWarning(
                    "Long-running SQL ({ElapsedMilliseconds}ms). Query={Query} QueryStoreStats={QueryStoreStats}",
                    executionTime,
                    queryText,
                    "Skipped: diagnostic concurrency limit reached.");
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    // CancellationToken fires at 2s; CommandTimeout at 5s is backup.
                    using var loggingCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

                    await LogQueryStoreByTextAsync(
                        queryText,
                        isStoredProcedure,
                        QueryStoreLookupTimeoutSeconds,
                        executionTime,
                        loggingCts.Token);

                    // A single success closes the breaker and clears the consecutive-failure count.
                    RecordQueryStoreSuccess();
                }
                catch (Exception ex)
                {
                    // The Query Store lookup is best-effort diagnostics. Swallow any failure so the
                    // fire-and-forget task never surfaces an unobserved exception. The exception is
                    // passed to the logger (queryable via env_ex_* columns), so it isn't repeated in the message.
                    // Count the failure toward the circuit breaker so a truly overloaded DB trips it.
                    RecordQueryStoreFailure();

                    _logger.LogWarning(
                        ex,
                        "Long-running SQL ({ElapsedMilliseconds}ms). Query={Query} QueryStoreStats={QueryStoreStats}",
                        executionTime,
                        queryText,
                        "Query Store lookup failed.");
                }
                finally
                {
                    // Always release the slot, even if the lookup threw, so the diagnostic gate
                    // can't leak slots and permanently disable long-running query logging.
                    _queryStoreLookupGate.Release();
                }
            });
        }

        /// <summary>
        /// Diagnostic circuit breaker gate. Returns <c>true</c> when a Query Store lookup is allowed
        /// to proceed. The breaker is "open" (returns <c>false</c>) once
        /// <see cref="QueryStoreCircuitBreakerFailureThreshold"/> consecutive failures have occurred,
        /// and stays open until the <see cref="QueryStoreCircuitBreakerCooldown"/> deadline. When the
        /// cooldown elapses, the first caller to observe it atomically clears the deadline and the
        /// breaker closes, so subsequent callers proceed as well (bounded by the concurrency gate).
        /// The consecutive-failure counter is not reset by this transition, so the next
        /// <see cref="RecordQueryStoreFailure"/> immediately re-opens the breaker, while any
        /// <see cref="RecordQueryStoreSuccess"/> fully resets it.
        /// </summary>
        internal static bool TryEnterQueryStoreCircuit()
        {
            long openUntil = Interlocked.Read(ref _queryStoreCircuitOpenUntilTicks);
            if (openUntil == 0)
            {
                // Breaker closed — normal operation.
                return true;
            }

            if (DateTime.UtcNow.Ticks < openUntil)
            {
                // Still within the cooldown window — stay open.
                return false;
            }

            // Cooldown elapsed. Close the breaker by atomically clearing the deadline. The caller that
            // wins the CAS performs the transition and proceeds; a concurrent caller that reads the
            // stale deadline loses the CAS and is skipped for this pass, but any later caller reads 0
            // (closed) and proceeds normally. The failure counter is left intact, so a subsequent
            // failure re-opens the breaker while a success resets it.
            return Interlocked.CompareExchange(ref _queryStoreCircuitOpenUntilTicks, 0, openUntil) == openUntil;
        }

        /// <summary>
        /// Records a successful Query Store lookup: resets the consecutive-failure counter and closes
        /// the circuit breaker. Any single success from any thread fully recovers the breaker.
        /// </summary>
        internal static void RecordQueryStoreSuccess()
        {
            Interlocked.Exchange(ref _queryStoreConsecutiveFailures, 0);
            Interlocked.Exchange(ref _queryStoreCircuitOpenUntilTicks, 0);
        }

        /// <summary>
        /// Records a failed/timed-out Query Store lookup. Once the consecutive-failure count reaches
        /// <see cref="QueryStoreCircuitBreakerFailureThreshold"/>, the breaker opens for
        /// <see cref="QueryStoreCircuitBreakerCooldown"/>. Because the count is not reset on the
        /// open-to-closed transition, the first failure after a cooldown re-opens the breaker for
        /// another window.
        /// </summary>
        internal static void RecordQueryStoreFailure()
        {
            int failures = Interlocked.Increment(ref _queryStoreConsecutiveFailures);
            if (failures >= QueryStoreCircuitBreakerFailureThreshold)
            {
                Interlocked.Exchange(
                    ref _queryStoreCircuitOpenUntilTicks,
                    DateTime.UtcNow.Add(QueryStoreCircuitBreakerCooldown).Ticks);
            }
        }

        /// <summary>
        /// Test-only seam: deterministically seeds the diagnostic circuit breaker's static state so
        /// unit tests can exercise the open/cooldown/probe transitions without waiting real time.
        /// </summary>
        internal static void SetQueryStoreCircuitStateForTests(int consecutiveFailures, long openUntilTicks)
        {
            Interlocked.Exchange(ref _queryStoreConsecutiveFailures, consecutiveFailures);
            Interlocked.Exchange(ref _queryStoreCircuitOpenUntilTicks, openUntilTicks);
        }

        /// <summary>
        /// Test-only seam: reads the diagnostic circuit breaker's open deadline (0 when closed) so
        /// unit tests can assert that a probe cleared it.
        /// </summary>
        internal static long GetQueryStoreCircuitOpenUntilTicksForTests()
        {
            return Interlocked.Read(ref _queryStoreCircuitOpenUntilTicks);
        }

        private async Task LogQueryStoreByTextAsync(
            string queryText,
            bool isStoredProcedure,
            int timeoutSeconds,
            long executionTime,
            CancellationToken ct)
        {
            // Create a NEW connection for this diagnostic query
            await _sqlRetryService.ExecuteSql(
                async (connection, cancel, sqlException) =>
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandType = CommandType.Text;
                    cmd.CommandTimeout = timeoutSeconds;

                    var sb = new StringBuilder();

                    if (isStoredProcedure)
                    {
                        // For stored procedures, use OBJECT_ID to filter directly by the procedure's
                        // hash/identity in Query Store. This avoids the expensive LIKE scan on
                        // query_sql_text entirely, since Query Store records object_id for every
                        // statement executed inside a stored procedure.
                        string procName = StripDboSchemaPrefix(queryText);

                        cmd.CommandText = @"
                DECLARE @CutoffTime datetimeoffset = DATEADD(HOUR, -1, SYSUTCDATETIME());

                SELECT TOP (5)
                    rs.count_executions,
                    rs.avg_duration / 1000.0 AS avg_duration_ms,
                    rs.avg_cpu_time / 1000.0 AS avg_cpu_ms,
                    rs.avg_logical_io_reads,
                    rs.avg_physical_io_reads,
                    rs.avg_logical_io_writes,
                    rs.avg_rowcount,
                    rs.max_duration / 1000.0 AS max_duration_ms,
                    rs.last_execution_time,
                    p.plan_id,
                    q.query_id
                FROM sys.query_store_query q
                JOIN sys.query_store_plan p ON p.query_id = q.query_id
                JOIN sys.query_store_runtime_stats rs ON rs.plan_id = p.plan_id
                WHERE q.object_id = OBJECT_ID(@ProcName)
                    AND rs.last_execution_time >= @CutoffTime
                ORDER BY rs.last_execution_time DESC;";

                        cmd.Parameters.AddWithValue("@ProcName", procName);

                        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                        await AppendQueryStoreResults(reader, sb, 0, 1, "StoredProc", ct);
                    }
                    else
                    {
                        // For ad-hoc queries, split into fragments (include queries have 2 statements
                        // split at INSERT INTO @FilteredData). For each fragment individually:
                        //  - If it contains a parameter hash comment: use the hash for a fast LIKE lookup
                        //  - If hash lookup returns nothing: fall back to the expensive REPLACE+LIKE
                        //  - If it has no hash: filter OUT hash-bearing rows to reduce the LIKE scan set
                        var normalizedText = StripQueryPreambleLines(queryText);
                        var searchFragments = SplitIntoSearchFragments(normalizedText);

                        // NOTE: The three lookup SQL strings below (HashLookupSql,
                        // TextLookupWithHashExclusionSql, TextLookupSql) share an identical
                        // SELECT / FROM / JOIN / ORDER BY structure and only differ in their WHERE
                        // clause. Any column, cutoff-window, or index-hint change must be applied to
                        // all three to avoid drift.
                        //
                        // SQL for the fast hash-based lookup (no REPLACE chain needed).
                        const string HashLookupSql = @"
                DECLARE @CutoffTime datetimeoffset = DATEADD(HOUR, -1, SYSUTCDATETIME());

                SELECT TOP (5)
                    rs.count_executions,
                    rs.avg_duration / 1000.0 AS avg_duration_ms,
                    rs.avg_cpu_time / 1000.0 AS avg_cpu_ms,
                    rs.avg_logical_io_reads,
                    rs.avg_physical_io_reads,
                    rs.avg_logical_io_writes,
                    rs.avg_rowcount,
                    rs.max_duration / 1000.0 AS max_duration_ms,
                    rs.last_execution_time,
                    p.plan_id,
                    q.query_id
                FROM sys.query_store_query_text qt
                JOIN sys.query_store_query q ON q.query_text_id = qt.query_text_id
                JOIN sys.query_store_plan p ON p.query_id = q.query_id
                JOIN sys.query_store_runtime_stats rs ON rs.plan_id = p.plan_id
                WHERE qt.query_sql_text LIKE '%' + @HashFilter + '%'
                    AND rs.last_execution_time >= @CutoffTime
                ORDER BY rs.last_execution_time DESC;";

                        // SQL for the expensive REPLACE+LIKE fallback.
                        // For fragments without a hash, also filter OUT hash-bearing rows to reduce scan set.
                        const string TextLookupWithHashExclusionSql = @"
                DECLARE @CutoffTime datetimeoffset = DATEADD(HOUR, -1, SYSUTCDATETIME());

                SELECT TOP (5)
                    rs.count_executions,
                    rs.avg_duration / 1000.0 AS avg_duration_ms,
                    rs.avg_cpu_time / 1000.0 AS avg_cpu_ms,
                    rs.avg_logical_io_reads,
                    rs.avg_physical_io_reads,
                    rs.avg_logical_io_writes,
                    rs.avg_rowcount,
                    rs.max_duration / 1000.0 AS max_duration_ms,
                    rs.last_execution_time,
                    p.plan_id,
                    q.query_id
                FROM sys.query_store_query_text qt
                JOIN sys.query_store_query q ON q.query_text_id = qt.query_text_id
                JOIN sys.query_store_plan p ON p.query_id = q.query_id
                JOIN sys.query_store_runtime_stats rs ON rs.plan_id = p.plan_id
                WHERE @NormalizedText <> ''
                    -- The '/* HASH ' literal below must stay in sync with
                    -- SqlQueryGenerator.ParametersHashStart (SQL const strings cannot reference the C# constant).
                    AND qt.query_sql_text NOT LIKE '%/* HASH %'
                    AND replace(replace(replace(replace(replace(replace(qt.query_sql_text, char(9), ''), char(10), ''), char(11), ''), char(12), ''), char(13), ''), char(32), '') LIKE '%' + @NormalizedText + '%'
                    AND rs.last_execution_time >= @CutoffTime
                ORDER BY rs.last_execution_time DESC;";

                        // SQL for the expensive REPLACE+LIKE fallback (no hash exclusion,
                        // used when hash lookup found nothing for a hash-bearing fragment).
                        const string TextLookupSql = @"
                DECLARE @CutoffTime datetimeoffset = DATEADD(HOUR, -1, SYSUTCDATETIME());

                SELECT TOP (5)
                    rs.count_executions,
                    rs.avg_duration / 1000.0 AS avg_duration_ms,
                    rs.avg_cpu_time / 1000.0 AS avg_cpu_ms,
                    rs.avg_logical_io_reads,
                    rs.avg_physical_io_reads,
                    rs.avg_logical_io_writes,
                    rs.avg_rowcount,
                    rs.max_duration / 1000.0 AS max_duration_ms,
                    rs.last_execution_time,
                    p.plan_id,
                    q.query_id
                FROM sys.query_store_query_text qt
                JOIN sys.query_store_query q ON q.query_text_id = qt.query_text_id
                JOIN sys.query_store_plan p ON p.query_id = q.query_id
                JOIN sys.query_store_runtime_stats rs ON rs.plan_id = p.plan_id
                WHERE @NormalizedText <> ''
                    AND replace(replace(replace(replace(replace(replace(qt.query_sql_text, char(9), ''), char(10), ''), char(11), ''), char(12), ''), char(13), ''), char(32), '') LIKE '%' + @NormalizedText + '%'
                    AND rs.last_execution_time >= @CutoffTime
                ORDER BY rs.last_execution_time DESC;";

                        for (int segmentIndex = 0; segmentIndex < searchFragments.Count; segmentIndex++)
                        {
                            string searchFragment = searchFragments[segmentIndex];

                            // Check each fragment individually for an embedded parameter hash.
                            // Include queries split into 2 fragments: fragment 1 (before INSERT INTO @FilteredData)
                            // typically has no hash, fragment 2 (after) has the hash comment.
                            string fragmentHash = ExtractParameterHash(searchFragment);
                            bool fragmentHasHash = fragmentHash != null;
                            int matchCount = 0;

                            if (fragmentHasHash)
                            {
                                // Fast path: search by the embedded parameter hash string.
                                cmd.CommandText = HashLookupSql;
                                cmd.Parameters.Clear();
                                string hashFilter = SqlSearchConstants.ParametersHashStart + fragmentHash;
                                cmd.Parameters.AddWithValue("@HashFilter", hashFilter);

                                using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                                matchCount = await AppendQueryStoreResults(reader, sb, segmentIndex, searchFragments.Count, "Hash", ct);
                            }

                            // Fall back to REPLACE+LIKE if hash lookup found nothing or fragment has no hash.
                            if (matchCount == 0)
                            {
                                string strippedFragment = StripAllWhitespace(searchFragment);

                                if (strippedFragment.Length > 4000)
                                {
                                    strippedFragment = strippedFragment[..4000];
                                }

                                // Fragments without a hash: exclude hash-bearing query store rows.
                                // Fragments with a hash that had no hash match: search all rows as fallback.
                                if (fragmentHasHash)
                                {
                                    cmd.CommandText = TextLookupSql;
                                }
                                else
                                {
                                    cmd.CommandText = TextLookupWithHashExclusionSql;
                                }

                                cmd.Parameters.Clear();
                                cmd.Parameters.AddWithValue("@NormalizedText", strippedFragment);

                                using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                                await AppendQueryStoreResults(reader, sb, segmentIndex, searchFragments.Count, fragmentHasHash ? "TextFallback" : "TextNoHash", ct);
                            }
                        }
                    }

                    if (sb.Length > 0)
                    {
                        _logger.LogWarning(
                            "Long-running SQL ({ElapsedMilliseconds}ms). Query={Query} QueryStoreStats={QueryStoreStats}",
                            executionTime,
                            queryText,
                            sb.ToString());
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Long-running SQL ({ElapsedMilliseconds}ms). Query={Query} QueryStoreStats={QueryStoreStats}",
                            executionTime,
                            queryText,
                            "No Query Store matches found.");
                    }
                },
                _logger,
                ct,
                isReadOnly: true);
        }

        /// <summary>
        /// Reads Query Store results from a <see cref="SqlDataReader"/> and appends formatted
        /// stats to the <paramref name="sb"/>. Returns the number of matches read.
        /// </summary>
        private static async Task<int> AppendQueryStoreResults(
            SqlDataReader reader,
            StringBuilder sb,
            int segmentIndex,
            int totalSegments,
            string lookupMethod,
            CancellationToken ct)
        {
            int matchIndex = 0;
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (await reader.IsDBNullAsync(0, ct).ConfigureAwait(false))
                {
                    continue;
                }

                matchIndex++;
                long planId = reader.GetInt64(9);
                long queryId = reader.GetInt64(10);

                string prefix = totalSegments > 1
                    ? $"  batch[{segmentIndex + 1}] match[{matchIndex}]"
                    : $"  match[{matchIndex}]";

                sb.AppendLine()
                  .Append(prefix)
                  .Append($" lookup={lookupMethod}")
                  .Append($" execs={reader.GetInt64(0)}")
                  .Append($" avgDurMs={Convert.ToDouble(reader.GetValue(1)):F1}")
                  .Append($" avgCpuMs={Convert.ToDouble(reader.GetValue(2)):F1}")
                  .Append($" avgLReads={Convert.ToDouble(reader.GetValue(3)):F0}")
                  .Append($" avgPReads={Convert.ToDouble(reader.GetValue(4)):F0}")
                  .Append($" avgLWrites={Convert.ToDouble(reader.GetValue(5)):F0}")
                  .Append($" avgRows={Convert.ToDouble(reader.GetValue(6)):F0}")
                  .Append($" maxDurMs={Convert.ToDouble(reader.GetValue(7)):F1}")
                  .Append($" lastExec={reader.GetDateTimeOffset(8):o}")
                  .Append($" queryId={queryId}")
                  .Append($" planId={planId}");
            }

            return matchIndex;
        }
    }
}
