// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.Common;
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
            int exceptionNumber = 0;

            if (exception is SqlTruncateException sqlTruncateException)
            {
                _logger.LogError(exception, $"A {nameof(ResourceSqlTruncateException)} occurred while executing request");

                throw new ResourceSqlTruncateException(sqlTruncateException.Message, exception);
            }
            else if (exception is SqlException sqlException)
            {
                // SqlException is not mockable - unable to test without complex reflection.
                // This logic must be similar to the DbException logic below which is only used for unit tests.
                _logger.LogError(exception, $"A {nameof(SqlException)} occurred while executing request");
                exceptionNumber = sqlException.Number;
            }
            else if (exception is DbException dbException)
            {
                // Only used for unit tests.
                _logger.LogError(dbException, $"A {nameof(DbException)} occurred while executing request");
                exceptionNumber = dbException.ErrorCode;
            }
            else
            {
                return Task.CompletedTask;
            }

            if (exceptionNumber == SqlErrorCodes.TimeoutExpired)
            {
                throw new RequestTimeoutException(Resources.ExecutionTimeoutExpired, exception);
            }
            else if (exceptionNumber == SqlErrorCodes.MethodNotAllowed)
            {
                throw new MethodNotAllowedException(Core.Resources.ResourceCreationNotAllowed, exception);
            }
            else if (exceptionNumber == SqlErrorCodes.QueryProcessorNoQueryPlan)
            {
                throw new SqlQueryPlanException(Core.Resources.SqlQueryProcessorRanOutOfInternalResourcesException, exception);
            }
            else if (exceptionNumber == LoginFailedForUser)
            {
                throw new LoginFailedForUserException(Core.Resources.InternalServerError, exception);
            }
            else if (IsCmkError(exceptionNumber))
            {
                throw new CustomerManagedKeyException(Core.Resources.OperationFailedForCustomerManagedKey);
            }
            else
            {
                throw new ResourceSqlException(Core.Resources.InternalServerError, exception);
            }
        }

        private static bool IsCmkError(int errorCode)
        {
            return errorCode is SqlErrorCodes.KeyVaultCriticalError or
                   SqlErrorCodes.KeyVaultEncounteredError or
                   SqlErrorCodes.KeyVaultErrorObtainingInfo or
                   SqlErrorCodes.CannotConnectToDBInCurrentState;
        }
    }
}
