// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using static Microsoft.Health.Fhir.SqlServer.Features.Storage.SqlRetryService;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public interface ISqlRetryService
    {
        Task<TResult> ExecuteSqlCommandFuncWithRetries<TResult, TLogger>(RetriableSqlCommandFunc<TResult> func, ILogger<TLogger> logger, string logMessage, CancellationToken cancellationToken);

        Task ExecuteSqlCommandActionWithRetries<TLogger>(RetriableSqlCommandAction action, ILogger<TLogger> logger, string logMessage, CancellationToken cancellationToken);

        Task ExecuteWithRetries(RetriableAction action, CancellationToken cancellationToken);
    }
}
