// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.FhirPackages
{
    /// <summary>
    /// SQL Server implementation of <see cref="ISearchParameterPackageStoreCheck"/>.
    /// </summary>
    /// <remarks>
    /// Applies the same rule as SearchParameter status initialization, which enables new parameters only when
    /// <c>dbo.Resource</c> is empty, except that SearchParameter rows are ignored. The read holds range locks, so a
    /// concurrent write of any other resource waits until the package transaction ends.
    /// </remarks>
    public sealed class SqlServerSearchParameterPackageStoreCheck : ISearchParameterPackageStoreCheck
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly SqlTransactionHandler _sqlTransactionHandler;
        private readonly ISqlServerFhirModel _model;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlServerSearchParameterPackageStoreCheck"/> class.
        /// </summary>
        /// <param name="sqlConnectionWrapperFactory">Provides the connection enlisted in the ambient transaction.</param>
        /// <param name="sqlTransactionHandler">The ambient transaction handler.</param>
        /// <param name="model">Resolves the SearchParameter resource type id.</param>
        public SqlServerSearchParameterPackageStoreCheck(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            SqlTransactionHandler sqlTransactionHandler,
            ISqlServerFhirModel model)
        {
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _sqlTransactionHandler = EnsureArg.IsNotNull(sqlTransactionHandler, nameof(sqlTransactionHandler));
            _model = EnsureArg.IsNotNull(model, nameof(model));
        }

        /// <inheritdoc />
        public async Task<bool> ContainsDataAsync(CancellationToken cancellationToken)
        {
            if (_sqlTransactionHandler.SqlTransactionScope == null)
            {
                throw new InvalidOperationException("The empty-store check must run inside the transaction that writes the package.");
            }

            using SqlConnectionWrapper conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, enlistInTransaction: true);
            using var cmd = new SqlCommand
            {
                CommandType = CommandType.Text,

                // HOLDLOCK keeps range locks until the transaction ends, so no counted row can be inserted before it commits.
                CommandText = "SELECT TOP 1 1 FROM dbo.Resource WITH (HOLDLOCK) WHERE ResourceTypeId <> @SearchParameterTypeId",
                Connection = conn.SqlConnection,
            };
            cmd.Parameters.Add(new SqlParameter("@SearchParameterTypeId", SqlDbType.SmallInt) { Value = _model.GetResourceTypeId(KnownResourceTypes.SearchParameter) });
            cmd.Transaction = conn.SqlTransaction;

            return await cmd.ExecuteScalarAsync(cancellationToken) != null;
        }
    }
}
