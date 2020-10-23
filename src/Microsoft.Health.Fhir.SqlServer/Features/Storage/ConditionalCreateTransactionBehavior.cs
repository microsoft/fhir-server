// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Abstractions.Features.Transactions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Storage;
using IsolationLevel = System.Data.IsolationLevel;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Intercepts ConditionalCreateResourceRequests and wraps them in a SQL Transaction
    /// </summary>
    public class ConditionalCreateTransactionBehavior
        : IPipelineBehavior<ConditionalCreateResourceRequest, UpsertResourceResponse>,
            IPipelineBehavior<ConditionalUpsertResourceRequest, UpsertResourceResponse>
    {
        private readonly ITransactionHandler _transactionHandler;
        private readonly SqlConnectionWrapperFactory _wrapperFactory;

        public ConditionalCreateTransactionBehavior(
            ITransactionHandler transactionHandler,
            SqlConnectionWrapperFactory wrapperFactory)
        {
            EnsureArg.IsNotNull(transactionHandler, nameof(transactionHandler));
            EnsureArg.IsNotNull(wrapperFactory, nameof(wrapperFactory));

            _transactionHandler = transactionHandler;
            _wrapperFactory = wrapperFactory;
        }

        public async Task<UpsertResourceResponse> Handle(ConditionalCreateResourceRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<UpsertResourceResponse> next)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            EnsureArg.IsNotNull(next, nameof(next));

            return await Execute(next, cancellationToken);
        }

        public async Task<UpsertResourceResponse> Handle(ConditionalUpsertResourceRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<UpsertResourceResponse> next)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            EnsureArg.IsNotNull(next, nameof(next));

            return await Execute(next, cancellationToken);
        }

        private async Task<UpsertResourceResponse> Execute(RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(next, nameof(next));

            UpsertResourceResponse result;
            try
            {
                SqlTransactionScope transactionScope = null;

                try
                {
                    if (_transactionHandler is SqlTransactionHandler sqlTransactionHandler && sqlTransactionHandler.SqlTransactionScope == null)
                    {
                        transactionScope = (SqlTransactionScope)sqlTransactionHandler.BeginTransaction();

                        // Create a transaction only if one doesn't already exist. This could be executed from within Bundle processing
                        if (transactionScope.SqlTransaction == null)
                        {
                            // These variables will be disposed by transactionScope
                            SqlConnectionWrapper wrapper = await _wrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken);
                            SqlTransaction trans = wrapper.SqlConnection.BeginTransaction(IsolationLevel.Serializable);

                            transactionScope.SqlConnection = wrapper.SqlConnection;
                            transactionScope.SqlTransaction = trans;
                        }
                    }

                    result = await next();
                    transactionScope?.Complete();
                }
                finally
                {
                    transactionScope?.Dispose();
                }

                return result;
            }
            catch (SqlException ex) when (ex.Number == 1205)
            {
                // Handles threads that were rejected by a SQL deadlock on the current resources
                // 1205: https://docs.microsoft.com/en-us/sql/relational-databases/errors-events/mssqlserver-1205-database-engine-error?view=sql-server-ver15
                throw new ResourceConflictException(Core.Resources.ResourceConflict);
            }
        }
    }
}
