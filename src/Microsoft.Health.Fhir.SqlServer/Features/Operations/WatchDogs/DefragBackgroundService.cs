// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations
{
    /// <summary>
    /// The background service used to host the <see cref="DefragWorker"/>.
    /// </summary>
    public class DefragBackgroundService : BackgroundService
    {
        private readonly DefragWorker _defragWorker;

        public DefragBackgroundService(DefragWorker defragWorker)
        {
            _defragWorker = EnsureArg.IsNotNull(defragWorker, nameof(defragWorker));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _defragWorker.ExecuteAsync(stoppingToken);
        }
    }
}
