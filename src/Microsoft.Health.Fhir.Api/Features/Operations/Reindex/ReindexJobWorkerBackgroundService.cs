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
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;

namespace Microsoft.Health.Fhir.Api.Features.Operations.Reindex
{
    /// <summary>
    /// The background service used to host the <see cref="ReindexJobWorker"/>.
    /// </summary>
    public class ReindexJobWorkerBackgroundService : BackgroundService
    {
        private readonly ReindexJobWorker _reindexJobWorker;
        private readonly ReindexJobConfiguration _reindexJobConfiguration;

        public ReindexJobWorkerBackgroundService(ReindexJobWorker reindexJobWorker, IOptions<ReindexJobConfiguration> reindexJobConfiguration)
        {
            EnsureArg.IsNotNull(reindexJobWorker, nameof(reindexJobWorker));
            EnsureArg.IsNotNull(reindexJobConfiguration?.Value, nameof(reindexJobConfiguration));

            _reindexJobWorker = reindexJobWorker;
            _reindexJobConfiguration = reindexJobConfiguration.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            if (_reindexJobConfiguration.Enabled)
            {
                await _reindexJobWorker.ExecuteAsync(cancellationToken);
            }
        }
    }
}
