// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Build.Framework;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Conformance.Serialization;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Storage;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlServerFhirOperationDataStore : FhirOperationDataStoreBase, ILegacyReindexOperationDataStore
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ILogger<SqlServerFhirOperationDataStore> _logger;
        private readonly JsonSerializerSettings _jsonSerializerSettings;
        private readonly IQueueClient _queueClient;

        public SqlServerFhirOperationDataStore(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            IQueueClient queueClient,
            ILogger<SqlServerFhirOperationDataStore> logger,
            ILoggerFactory loggerFactory)
            : base(queueClient, loggerFactory)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _queueClient = queueClient;
            _logger = logger;

            _jsonSerializerSettings = new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>
                {
                    new EnumLiteralJsonConverter(),
                },
            };
         }

        public override async Task<ReindexJobWrapper> GetReindexJobByIdAsync(string jobId, CancellationToken cancellationToken)
        {
            if (IsLegacyJob(jobId))
            {
                // try old job records
                var oldJobs = (ILegacyReindexOperationDataStore)this;
                return await oldJobs.GetLegacyReindexJobByIdAsync(jobId, cancellationToken);
            }

            return await base.GetReindexJobByIdAsync(jobId, cancellationToken);
        }

        private static bool IsLegacyJob(string jobId)
        {
            return !long.TryParse(jobId, out long _);
        }

        async Task<ReindexJobWrapper> ILegacyReindexOperationDataStore.GetLegacyReindexJobByIdAsync(string id, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(id, nameof(id));

            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.GetReindexJobById.PopulateCommand(sqlCommandWrapper, id);

                using (SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    if (!await sqlDataReader.ReadAsync(cancellationToken))
                    {
                        throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, id));
                    }

                    (string rawJobRecord, byte[] rowVersion) = sqlDataReader.ReadRow(VLatest.ReindexJob.RawJobRecord, VLatest.ReindexJob.JobVersion);

                    return CreateReindexJobWrapper(rawJobRecord, rowVersion);
                }
            }
        }

        private static WeakETag GetRowVersionAsEtag(byte[] rowVersionAsBytes)
        {
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(rowVersionAsBytes);
            }

            var rowVersionAsDecimalString = BitConverter.ToInt64(rowVersionAsBytes, startIndex: 0).ToString();

            return WeakETag.FromVersionId(rowVersionAsDecimalString);
        }

        private ReindexJobWrapper CreateReindexJobWrapper(string rawJobRecord, byte[] rowVersionAsBytes)
        {
            var reindexJobRecord = JsonConvert.DeserializeObject<ReindexJobRecord>(rawJobRecord, _jsonSerializerSettings);

            WeakETag etag = GetRowVersionAsEtag(rowVersionAsBytes);

            return new ReindexJobWrapper(reindexJobRecord, etag);
        }
    }
}
