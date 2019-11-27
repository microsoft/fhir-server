// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.IO;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlServerFhirOperationDataStore : IFhirOperationDataStore
    {
        internal static readonly Encoding ResourceEncoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);

        private readonly SqlServerDataStoreConfiguration _configuration;
        private readonly ILogger<SqlServerFhirOperationDataStore> _logger;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;

        public SqlServerFhirOperationDataStore(SqlServerDataStoreConfiguration configuration, ILogger<SqlServerFhirOperationDataStore> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _memoryStreamManager = new RecyclableMemoryStreamManager();
        }

        public Task<IReadOnlyCollection<ExportJobOutcome>> AcquireExportJobsAsync(ushort maximumNumberOfConcurrentJobsAllowed, TimeSpan jobHeartbeatTimeoutThreshold, CancellationToken cancellationToken)
        {
            IReadOnlyCollection<ExportJobOutcome> returnValue = new List<ExportJobOutcome>();
            return Task.FromResult(returnValue);
        }

        public async Task<ExportJobOutcome> CreateExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken)
        {
            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (SqlCommand command = connection.CreateCommand())
                using (var stream = new RecyclableMemoryStream(_memoryStreamManager))
                using (var gzipStream = new GZipStream(stream, CompressionMode.Compress))
                using (var writer = new StreamWriter(gzipStream, ResourceEncoding))
                {
                    var rawJobRecord = JsonConvert.SerializeObject(jobRecord);
                    writer.Write(rawJobRecord);
                    writer.Flush();

                    stream.Seek(0, 0);
                    /*
                    V1.CreateExportJob.PopulateCommand(
                        command,
                        jobRecord.Id,
                        jobRecord.Status.ToString(),
                        stream);*/

                    var query = "INSERT INTO dbo.ExportJob (JobId, JobStatus, RawJobRecord)";
                    query += " VALUES (@jobId, @jobStatus, @rawJobRecord)";

                    command.CommandText = query;

                    // TODO: Where should this be set? Should this be set? Seems to be null on Cosmos side.
                    jobRecord.StartTime = DateTimeOffset.Now;

                    command.Parameters.AddWithValue("@jobId", jobRecord.Id);
                    command.Parameters.AddWithValue("@jobStatus", jobRecord.Status);
                    command.Parameters.AddWithValue("@rawJobRecord", stream); // TODO: On Cosmos side this is a zipped binary stream. handled in a stored procedure.

                    // TODO: Log SQL command.
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            // TODO: Where should the e tag come from?
            return new ExportJobOutcome(jobRecord, WeakETag.FromVersionId("1"));
        }

        public Task<ExportJobOutcome> GetExportJobByIdAsync(string id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public async Task<ExportJobOutcome> GetExportJobByHashAsync(string hash, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            return null;
        }

        public Task<ExportJobOutcome> UpdateExportJobAsync(ExportJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
