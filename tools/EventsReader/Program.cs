// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Configuration;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Internal.Fhir.EventsReader
{
    public static class Program
    {
        private static readonly string _connectionString = ConfigurationManager.ConnectionStrings["Database"].ConnectionString;
        private static readonly string Pwd = ConfigurationManager.AppSettings["Pwd"];
        private static SqlRetryService _sqlRetryService;
        private static SqlStoreClient<SqlServerFhirDataStore> _store;
        private static string _parameterId = "Events.LastProcessedTransactionId";

        public static void Main()
        {
            ISqlConnectionBuilder iSqlConnectionBuilder = new Sql.SqlConnectionBuilder(_connectionString);
            _sqlRetryService = SqlRetryService.GetInstance(iSqlConnectionBuilder);
            _store = new SqlStoreClient<SqlServerFhirDataStore>(_sqlRetryService, NullLogger<SqlServerFhirDataStore>.Instance);

            ////PingStorage("https://sergeyperfstandard.blob.core.windows.net/test", "ABC.txt");
            ////PingSelect();
            PingInsert();
            PingCopyInto("https://sergeyperfstandard.blob.core.windows.net/test/ABC.txt");

            ////ExecuteAsync().Wait();
        }

#pragma warning disable CA1303 // Do not pass literals as localized parameters
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
        private static void PingStorage(string container, string file)
        {
            var containerClient = new BlobContainerClient(new Uri(container), new ClientSecretCredential("72f988bf-86f1-41af-91ab-2d7cd011db47", "44b8fda8-39cf-4b87-b257-7f21205dbb71", Pwd));
            containerClient.CreateIfNotExists();
            var blobClient = containerClient.GetBlockBlobClient("ABC.txt");

            using var readStream = blobClient.OpenRead();
            using var reader = new StreamReader(readStream);
            var line = string.Empty;
            while (!reader.EndOfStream)
            {
                line = reader.ReadLine();
            }

            Console.WriteLine($"Reads from storage completed. {line}");

            using var writeStream = blobClient.OpenWrite(true);
            using var writer = new StreamWriter(writeStream);
            line = line == "ABC" ? "XYZ" : line == "XYZ" ? "ABC" : line;
            writer.WriteLine(line);
            Console.WriteLine($"Writes to storage completed. {line}");

            using var readStream2 = blobClient.OpenRead();
            using var reader2 = new StreamReader(readStream2);
            while (!reader2.EndOfStream)
            {
                line = reader2.ReadLine();
            }

            Console.WriteLine($"Reads from storage after writes completed. {line}");
        }

#pragma warning disable CA1861 // Avoid constant arrays as arguments
#pragma warning disable IDE0300 // Simplify collection initialization
        private static void PingSelect()
        {
            var cred = new ClientSecretCredential("72f988bf-86f1-41af-91ab-2d7cd011db47", "44b8fda8-39cf-4b87-b257-7f21205dbb71", Pwd);
            var token = cred.GetToken(new TokenRequestContext(new[] { "https://database.windows.net/.default" }));
            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand("SELECT count(*) FROM Test", conn);
            conn.AccessToken = token.Token;
            conn.Open();
            var result = (int)cmd.ExecuteScalar();
            Console.WriteLine($"PingSelect completed: rows={result}");
        }

        private static void PingInsert()
        {
            ////var tenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47";
            ////var clientId = "44b8fda8-39cf-4b87-b257-7f21205dbb71";
            ////var secretClient = new SecretClient(new Uri("https://mshapis-test-ev2-kv.vault.azure.net/"), new DefaultAzureCredential());
            ////var secret = secretClient.GetSecretAsync("fhir-paas-ci-deployment").Result;
            ////var secretBytes = Convert.FromBase64String(secret.Value.Value);
            ////using var cert = new X509Certificate2(secretBytes, (string)null, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
            ////var cred = new ClientCertificateCredential(tenantId, clientId, cert, new ClientCertificateCredentialOptions { SendCertificateChain = true });
            var cred = new ClientSecretCredential("72f988bf-86f1-41af-91ab-2d7cd011db47", "44b8fda8-39cf-4b87-b257-7f21205dbb71", Pwd);
            var token = cred.GetToken(new TokenRequestContext(new[] { "https://database.windows.net/.default" }));
            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand("INSERT INTO Test SELECT 'XYZ'", conn);
            conn.AccessToken = token.Token;
            conn.Open();
            cmd.ExecuteNonQuery();
            Console.WriteLine($"PingInsert completed.");
        }

        private static void PingCopyInto(string file)
        {
            var cred = new ClientSecretCredential("72f988bf-86f1-41af-91ab-2d7cd011db47", "44b8fda8-39cf-4b87-b257-7f21205dbb71", Pwd);
            var token = cred.GetToken(new TokenRequestContext(new[] { "https://api.fabric.microsoft.com/.default" }));
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + token.Token);
            var response = httpClient.GetAsync(new Uri("https://api.fabric.microsoft.com/v1/workspaces")).Result;
            var json = response.Content.ReadAsStringAsync().Result;
            Console.WriteLine($"API call completed with status={response.StatusCode} json={json}");

            token = cred.GetToken(new TokenRequestContext(new[] { "https://database.windows.net/.default" }));
            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand($"COPY INTO Test FROM '{file}' WITH (FILE_TYPE = 'CSV')", conn);
            conn.AccessToken = token.Token;
            conn.Open();
            cmd.ExecuteNonQuery();
            Console.WriteLine($"PingCopyInto completed.");
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
