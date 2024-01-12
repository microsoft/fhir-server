// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Configuration;
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Internal.Fhir.EventsReader
{
    public static class Program
    {
        private static readonly string _connectionString = ConfigurationManager.ConnectionStrings["Database"].ConnectionString;
        private static SqlRetryService _sqlRetryService;
        private static SqlStoreClient<SqlServerFhirDataStore> _store;

        public static void Main()
        {
            ISqlConnectionBuilder iSqlConnectionBuilder = new Sql.SqlConnectionBuilder(_connectionString);
            _sqlRetryService = SqlRetryService.GetInstance(iSqlConnectionBuilder);
            _store = new SqlStoreClient<SqlServerFhirDataStore>(_sqlRetryService, NullLogger<SqlServerFhirDataStore>.Instance);

            var totalsKeys = 0L;
            var totalTrans = 0;
            var sw = Stopwatch.StartNew();
            var swReport = Stopwatch.StartNew();
            var visibility = 0L;
            while (true)
            {
                Thread.Sleep(3000);
                var currentVisibility = _store.MergeResourcesGetTransactionVisibilityAsync(CancellationToken.None).Result;
                if (currentVisibility > visibility)
                {
                    var transactions = _store.GetTransactionsAsync(visibility, currentVisibility, CancellationToken.None).Result;
                    Parallel.ForEach(transactions, new ParallelOptions { MaxDegreeOfParallelism = 64 }, transaction =>
                    {
                        var keys = _store.GetResourceDateKeysByTransactionIdAsync(transaction.TransactionId, CancellationToken.None).Result;
                        Interlocked.Add(ref totalsKeys, keys.Count);
                        Interlocked.Increment(ref totalTrans);

                        if (swReport.Elapsed.TotalSeconds > 60)
                        {
                            lock (swReport)
                            {
                                if (swReport.Elapsed.TotalSeconds > 60)
                                {
                                    Console.WriteLine($"secs={(int)sw.Elapsed.TotalSeconds} trans={totalTrans} total={totalsKeys} speed={totalsKeys / 1000.0 / sw.Elapsed.TotalSeconds} K KPS.");
                                    swReport.Restart();
                                }
                            }
                        }
                    });

                    Console.WriteLine($"secs={(int)sw.Elapsed.TotalSeconds} trans={totalTrans} total={totalsKeys} speed={totalsKeys / 1000.0 / sw.Elapsed.TotalSeconds} K KPS.");

                    visibility = currentVisibility;
                }
            }
        }
    }
}
