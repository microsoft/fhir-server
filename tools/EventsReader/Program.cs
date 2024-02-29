// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Configuration;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Internal.Fhir.EventsReader
{
    public static class Program
    {
        private static readonly string _connectionString = ConfigurationManager.ConnectionStrings["Database"].ConnectionString;
        private static SqlRetryService _sqlRetryService;
        private static SqlStoreClient _store;
        private static string _parameterId = "Events.LastProcessedTransactionId";

        public static void Main()
        {
            ISqlConnectionBuilder iSqlConnectionBuilder = new Sql.SqlConnectionBuilder(_connectionString);
            _sqlRetryService = SqlRetryService.GetInstance(iSqlConnectionBuilder);
            _store = new SqlStoreClient(_sqlRetryService, NullLogger<SqlStoreClient>.Instance);

            ExecuteAsync().Wait();
        }

        private static async Task ExecuteAsync()
        {
            var totalsKeys = 0L;
            var totalTrans = 0;
            var sw = Stopwatch.StartNew();
            var swReport = Stopwatch.StartNew();
            var visibility = await GetLastProcessedTransactionId();
            while (true)
            {
                await Task.Delay(3000);
                var currentVisibility = await _store.MergeResourcesGetTransactionVisibilityAsync(CancellationToken.None);
                Console.WriteLine($"lastProcessed={visibility} current={currentVisibility}");
                if (currentVisibility > visibility)
                {
                    var transactions = await _store.GetTransactionsAsync(visibility, currentVisibility, CancellationToken.None);
                    Console.WriteLine($"lastProcessed={visibility} current={currentVisibility} trans={transactions.Count}");
                    await Parallel.ForEachAsync(transactions, new ParallelOptions { MaxDegreeOfParallelism = 64 }, async (transaction, cancel) =>
                    {
                        var keys = await _store.GetResourceDateKeysByTransactionIdAsync(transaction.TransactionId, CancellationToken.None);
                        Interlocked.Add(ref totalsKeys, keys.Count);
                        Interlocked.Increment(ref totalTrans);

                        if (swReport.Elapsed.TotalSeconds > 30)
                        {
                            lock (swReport)
                            {
                                if (swReport.Elapsed.TotalSeconds > 30)
                                {
                                    Console.WriteLine($"secs={(int)sw.Elapsed.TotalSeconds} processedTrans={totalTrans} processedKeys={totalsKeys} speed={(int)(totalsKeys / 1000.0 / sw.Elapsed.TotalSeconds)} K KPS.");
                                    swReport.Restart();
                                }
                            }
                        }
                    });

                    Console.WriteLine($"secs={(int)sw.Elapsed.TotalSeconds} processedTrans={totalTrans} processedKeys={totalsKeys} speed={(int)(totalsKeys / 1000.0 / sw.Elapsed.TotalSeconds)} K KPS.");

                    await UpdateLastProcessedTransactionId(currentVisibility);

                    visibility = currentVisibility;
                }
            }
        }

        // POC temp method
        private static async Task<long> GetLastProcessedTransactionId()
        {
            using var cmd = new SqlCommand("SELECT Bigint FROM dbo.Parameters WHERE Id = @Id");
            cmd.Parameters.AddWithValue("@Id", _parameterId);
            var value = await cmd.ExecuteScalarAsync(_sqlRetryService, NullLogger<SqlServerFhirDataStore>.Instance, CancellationToken.None);
            return value != null ? (long)value : 0;
        }

        // POC temp method
        private static async Task UpdateLastProcessedTransactionId(long tranId)
        {
            using var cmd = new SqlCommand(@"
IF NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @Id)
  INSERT INTO dbo.Parameters (Id, Bigint) SELECT @Id, @TranId
ELSE
  UPDATE dbo.Parameters SET Bigint = @TranId WHERE Id = @Id
            ");
            cmd.Parameters.AddWithValue("@Id", _parameterId);
            cmd.Parameters.AddWithValue("@TranId", tranId);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, NullLogger<SqlServerFhirDataStore>.Instance, CancellationToken.None);
        }
    }
}
