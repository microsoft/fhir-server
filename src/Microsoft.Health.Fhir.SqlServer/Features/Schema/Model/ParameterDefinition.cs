// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Data.SqlClient;
using EnsureThat;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema.Model
{
    /// <summary>
    /// Represents a parameter definition (not the value) for a SQL stored procedure.
    /// </summary>
    /// <typeparam name="T">The CLR type of the parameter</typeparam>
    public class ParameterDefinition<T>
    {
        private readonly SqlDbType _type;
        private readonly bool _nullable;
        private readonly byte _precision;
        private readonly byte _scale;
        private readonly long _length;

        public ParameterDefinition(string name, SqlDbType type, bool nullable)
        {
            EnsureArg.IsNotNullOrWhiteSpace(name, nameof(name));
            Name = name;
            _type = type;
            _nullable = nullable;
        }

        public ParameterDefinition(string name, SqlDbType type, bool nullable, long length)
            : this(name, type, nullable)
        {
            _length = length;
        }

        public ParameterDefinition(string name, SqlDbType type, bool nullable, byte precision, byte scale)
            : this(name, type, nullable)
        {
            _precision = precision;
            _scale = scale;
        }

        /// <summary>
        /// Gets the parameter name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Adds a parameter to a <see cref="SqlCommand"/>'s parameter collection with a given value.
        /// </summary>
        /// <param name="parameters">The parameter collection</param>
        /// <param name="value">The parameter value</param>
        /// <returns>The parameter that was added to the collection.</returns>
        public virtual SqlParameter AddParameter(SqlParameterCollection parameters, T value)
        {
            EnsureArg.IsNotNull(parameters, nameof(parameters));

            return parameters.Add(
                new SqlParameter(
                    parameterName: Name,
                    dbType: _type,
                    size: (int)_length,
                    direction: ParameterDirection.Input,
                    isNullable: _nullable,
                    precision: _precision,
                    scale: _scale,
                    sourceColumn: null,
                    sourceVersion: DataRowVersion.Current,
                    value: value));
        }
    }
}
