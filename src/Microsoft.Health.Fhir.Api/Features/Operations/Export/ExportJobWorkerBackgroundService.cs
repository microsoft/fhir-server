// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;

namespace Microsoft.Health.Fhir.Api.Features.Operations.Export
{
    /// <summary>
    /// The background service used to host the <see cref="ExportJobWorker"/>.
    /// </summary>
    public class ExportJobWorkerBackgroundService : BackgroundService
    {
        private readonly ExportJobWorker _exportJobWorker;

        public ExportJobWorkerBackgroundService(ExportJobWorker exportJobWorker)
        {
            EnsureArg.IsNotNull(exportJobWorker, nameof(exportJobWorker));

            _exportJobWorker = exportJobWorker;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _exportJobWorker.ExecuteAsync(stoppingToken);
        }
    }
}
