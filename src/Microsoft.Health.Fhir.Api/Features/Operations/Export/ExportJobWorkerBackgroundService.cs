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
    /// The background service used to host the <see cref="ExportJobWorker"/>.
    /// </summary>
    public class ExportJobWorkerBackgroundService : BackgroundService
    {
        private readonly ExportJobWorker _exportJobWorker;
        private readonly ExportJobConfiguration _exportJobConfiguration;

        public ExportJobWorkerBackgroundService(ExportJobWorker exportJobWorker, IOptions<ExportJobConfiguration> exportJobConfiguration)
        {
            EnsureArg.IsNotNull(exportJobWorker, nameof(exportJobWorker));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));

            _exportJobWorker = exportJobWorker;
            _exportJobConfiguration = exportJobConfiguration.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_exportJobConfiguration.Enabled)
            {
                await _exportJobWorker.ExecuteAsync(stoppingToken);
            }
        }
    }
}
