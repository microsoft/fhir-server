// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.DataConvert;

namespace Microsoft.Health.Fhir.Core.Features.Operations.DataConvert
{
    public class DataConvertRequestHandler : IRequestHandler<DataConvertRequest, DataConvertResponse>
    {
        private readonly IFhirAuthorizationService _authorizationService;
        private readonly IDataConvertEngine _dataConvertEngine;
        private readonly DataConvertConfiguration _dataConvertConfiguration;

        public DataConvertRequestHandler(
            IFhirAuthorizationService authorizationService,
            IDataConvertEngine dataConvertEngine,
            IOptions<DataConvertConfiguration> dataConvertConfiguration)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(dataConvertEngine, nameof(dataConvertEngine));
            EnsureArg.IsNotNull(dataConvertConfiguration, nameof(dataConvertConfiguration));

            _authorizationService = authorizationService;
            _dataConvertEngine = dataConvertEngine;
            _dataConvertConfiguration = dataConvertConfiguration.Value;
        }

        public async Task<DataConvertResponse> Handle(DataConvertRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request);

            if (await _authorizationService.CheckAccess(DataActions.DataConvert) != DataActions.DataConvert)
            {
                throw new UnauthorizedFhirActionException();
            }

            return await ExecuteWithTimeoutAync(cancellationToken => _dataConvertEngine.Process(request, cancellationToken), _dataConvertConfiguration.ProcessTimeoutThreshold, cancellationToken);
        }

        /// <summary>
        /// This task can either be cancelled due to external cancellation or timeout.
        /// </summary>
        /// <typeparam name="TResult">Type of return value</typeparam>
        /// <param name="runTaskFactory">Task factory to get task</param>
        /// <param name="timeout">Time out span</param>
        /// <param name="cancellationToken">External cancellation token</param>
        /// <returns>The result of run task</returns>
        private async Task<TResult> ExecuteWithTimeoutAync<TResult>(
            Func<CancellationToken, Task<TResult>> runTaskFactory,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using (var timeoutCancellation = new CancellationTokenSource())
            using (var combinedCancellation = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token))
            {
                var runTask = runTaskFactory(combinedCancellation.Token);
                var completedTask = await Task.WhenAny(runTask, Task.Delay(timeout, timeoutCancellation.Token));

                // Cancel the task which is still running.
                timeoutCancellation.Cancel();
                if (completedTask == runTask)
                {
                    return await runTask;
                }
                else
                {
                    throw new DataConvertTimeoutException("The data convert operation has timed out.");
                }
            }
        }
    }
}
