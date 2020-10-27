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
            EnsureArg.IsNotNull(authorizationService);
            EnsureArg.IsNotNull(dataConvertEngine);
            EnsureArg.IsNotNull(dataConvertConfiguration);

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

            var dataConvertTask = _dataConvertEngine.Process(request);
            return await ExecuteWithTimeout(dataConvertTask, _dataConvertConfiguration.ProcessTimeoutThreshold, cancellationToken);
        }

        private async Task<TResult> ExecuteWithTimeout<TResult>(Task<TResult> runTask, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using (var timeoutCancellation = new CancellationTokenSource())
            using (var combinedCancellation = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token))
            {
                var completedTask = await Task.WhenAny(runTask, Task.Delay(timeout, cancellationToken));
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
