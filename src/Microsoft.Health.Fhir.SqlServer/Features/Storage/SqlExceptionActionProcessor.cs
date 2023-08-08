// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlTypes;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR.Pipeline;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlExceptionActionProcessor<TRequest, TException> : IRequestExceptionAction<TRequest, TException>
        where TException : Exception
    {
        private readonly ILogger<SqlExceptionActionProcessor<TRequest, TException>> _logger;
        private const int LoginFailedForUser = 18456;

        public SqlExceptionActionProcessor(ILogger<SqlExceptionActionProcessor<TRequest, TException>> logger)
        {
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public Task Execute(TRequest request, TException exception, CancellationToken cancellationToken)
        {
            if (exception is SqlException sqlException)
            {
                _logger.LogError(exception, $"A {nameof(SqlException)} occurred while executing request");

                if (sqlException.Number == SqlErrorCodes.TimeoutExpired)
                {
                    throw new RequestTimeoutException(Resources.ExecutionTimeoutExpired, exception);
                }
                else if (sqlException.Number == SqlErrorCodes.MethodNotAllowed)
                {
                    throw new MethodNotAllowedException(Core.Resources.ResourceCreationNotAllowed, exception);
                }
                else if (sqlException.Number == SqlErrorCodes.QueryProcessorNoQueryPlan)
                {
                    throw new SqlQueryPlanException(Core.Resources.SqlQueryProcessorRanOutOfInternalResourcesException, exception);
                }
                else if (sqlException.Number == LoginFailedForUser)
                {
                    throw new LoginFailedForUserException(Core.Resources.InternalServerError, exception);
                }
                else
                {
                    throw new ResourceSqlException(Core.Resources.InternalServerError, exception);
                }
            }
            else if (exception is SqlTruncateException sqlTruncateException)
            {
                _logger.LogError(exception, $"A {nameof(ResourceSqlTruncateException)} occurred while executing request");

                throw new ResourceSqlTruncateException(sqlTruncateException.Message, exception);
            }

            return Task.CompletedTask;
        }
    }
}
