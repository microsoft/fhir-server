// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal sealed class SqlVectorSearchSourceDependencyStore : IVectorSearchSourceDependencyStore
    {
        private readonly SqlServerFhirModel _model;
        private readonly ISqlRetryService _sqlRetryService;
        private readonly ILogger<SqlVectorSearchSourceDependencyStore> _logger;

        public SqlVectorSearchSourceDependencyStore(
            SqlServerFhirModel model,
            ISqlRetryService sqlRetryService,
            ILogger<SqlVectorSearchSourceDependencyStore> logger)
        {
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task<IReadOnlyCollection<ResourceKey>> GetDependentResourceKeysAsync(
            string sourceResourceType,
            string sourceResourceId,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(sourceResourceType, nameof(sourceResourceType));
            EnsureArg.IsNotNullOrWhiteSpace(sourceResourceId, nameof(sourceResourceId));

            using var command = new SqlCommand("dbo.GetVectorSearchSourceDependencies")
            {
                CommandType = CommandType.StoredProcedure,
            };

            command.Parameters.AddWithValue("@SourceResourceTypeId", _model.GetResourceTypeId(sourceResourceType));
            command.Parameters.AddWithValue("@SourceResourceId", sourceResourceId);

            return await command.ExecuteReaderAsync(
                _sqlRetryService,
                reader => new ResourceKey(_model.GetResourceTypeName(reader.GetInt16(0)), reader.GetString(1)),
                _logger,
                cancellationToken);
        }
    }
}
