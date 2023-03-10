// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;

namespace Microsoft.Health.Fhir.Api.Features.Operations.Export
{
    /// <summary>
    /// The background service used to host the <see cref="LegacyExportJobWorker"/>.
    /// </summary>
    public class LegacyExportJobWorkerBackgroundService : BackgroundService
    {
        private readonly LegacyExportJobWorker _legacyExportJobWorker;
        private readonly ExportJobConfiguration _exportJobConfiguration;

        public LegacyExportJobWorkerBackgroundService(LegacyExportJobWorker legacyExportJobWorker, IOptions<ExportJobConfiguration> exportJobConfiguration)
        {
            EnsureArg.IsNotNull(legacyExportJobWorker, nameof(legacyExportJobWorker));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));

            _legacyExportJobWorker = legacyExportJobWorker;
            _exportJobConfiguration = exportJobConfiguration.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_exportJobConfiguration.Enabled)
            {
                await _legacyExportJobWorker.ExecuteAsync(stoppingToken);
            }
        }
    }
}
