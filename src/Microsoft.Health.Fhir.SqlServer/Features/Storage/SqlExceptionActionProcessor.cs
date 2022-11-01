// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlTypes;
using System.Threading;
using System.Threading.Tasks;
using MediatR.Pipeline;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlExceptionActionProcessor<TRequest, TException> : IRequestExceptionAction<TRequest, TException>
        where TException : Exception
    {
        private const int LoginFailedForUser = 18456;

        public Task Execute(TRequest request, TException exception, CancellationToken cancellationToken)
        {
            if (exception is SqlException sqlException)
            {
                if (sqlException.Number == SqlErrorCodes.TimeoutExpired)
                {
                    throw new RequestTimeoutException(Resources.ExecutionTimeoutExpired);
                }
                else if (sqlException.Number == SqlErrorCodes.MethodNotAllowed)
                {
                    throw new MethodNotAllowedException(Core.Resources.ResourceCreationNotAllowed);
                }
                else if (sqlException.Number == SqlErrorCodes.QueryProcessorNoQueryPlan)
                {
                    throw new SqlQueryPlanException(Core.Resources.SqlQueryProcessorRanOutOfInternalResourcesException);
                }
                else if (sqlException.Number == LoginFailedForUser)
                {
                    throw new LoginFailedForUserException(Core.Resources.LoginFailedForUser);
                }
                else
                {
                    throw new ResourceSqlException(Core.Resources.InternalServerError);
                }
            }
            else if (exception is SqlTruncateException sqlTruncateException)
            {
                throw new ResourceSqlTruncateException(sqlTruncateException.Message);
            }

            return Task.CompletedTask;
        }
    }
}
