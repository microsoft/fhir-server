// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data.SqlClient;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public static class SqlParameterCollectionExtensions
    {
        /// <summary>
        /// Adds a parameter to a <see cref="SqlParameterCollection"/> based on a <see cref="Column{T}"/> and value.
        /// The parameter's type, precision, and scale properties will be set based on the column's metadata.
        /// </summary>
        /// <typeparam name="T">The parameter type</typeparam>
        /// <param name="parameters">The parameters</param>
        /// <param name="column">The column from which metadata is derived</param>
        /// <param name="value">The parameter value</param>
        /// <param name="parameterName">The parameter name</param>
        /// <returns>The created parameter.</returns>
        public static SqlParameter AddFromColumn<T>(this SqlParameterCollection parameters, Column<T> column, T value, string parameterName)
        {
            return column.AddParameter(parameters, value, parameterName);
        }

        /// <summary>
        /// Adds a parameter to a <see cref="SqlParameterCollection"/> based on a <see cref="Column{T}"/> and value.
        /// The parameter's type, precision, and scale properties will be set based on the column's metadata.
        /// The name of the parameter is based on the column's name. For example, if the column is named
        /// "MyColumn" then the parameter will be named "@myColumn".
        /// </summary>
        /// <typeparam name="T">The parameter type</typeparam>
        /// <param name="parameters">The parameters</param>
        /// <param name="column">The column from which metadata is derived</param>
        /// <param name="value">The parameter value</param>
        /// <returns>The created parameter.</returns>
        public static SqlParameter AddFromColumnWithDefaultName<T>(this SqlParameterCollection parameters, Column<T> column, T value)
        {
            return column.AddParameterWithDefaultName(parameters, value);
        }
    }
}
