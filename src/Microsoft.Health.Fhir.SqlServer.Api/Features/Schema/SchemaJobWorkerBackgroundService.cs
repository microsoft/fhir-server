// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Api.Features.Schema
{
    /// <summary>
    /// The background service used to host the <see cref="SchemaJobWorker"/>.
    /// </summary>
    public class SchemaJobWorkerBackgroundService : BackgroundService
    {
        private readonly string _instanceName;
        private readonly SchemaJobWorker _schemaJobWorker;
        private readonly SqlServerDataStoreConfiguration _sqlServerDataStoreConfiguration;
        private readonly SchemaInformation _schemaInformation;

        public SchemaJobWorkerBackgroundService(SchemaJobWorker schemaJobWorker, SqlServerDataStoreConfiguration sqlServerDataStoreConfiguration, SchemaInformation schemaInformation)
        {
            EnsureArg.IsNotNull(schemaJobWorker, nameof(schemaJobWorker));
            EnsureArg.IsNotNull(sqlServerDataStoreConfiguration, nameof(sqlServerDataStoreConfiguration));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));

            _schemaJobWorker = schemaJobWorker;
            _sqlServerDataStoreConfiguration = sqlServerDataStoreConfiguration;
            _schemaInformation = schemaInformation;
            _instanceName = Guid.NewGuid() + "-" + Process.GetCurrentProcess().Id.ToString();
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            if (!_sqlServerDataStoreConfiguration.SchemaUpdatesEnabled)
            {
                await _schemaJobWorker.ExecuteAsync(_schemaInformation, _instanceName, cancellationToken);
            }
        }
    }
}
