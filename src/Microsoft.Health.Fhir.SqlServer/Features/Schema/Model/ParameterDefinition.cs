// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Data.SqlClient;
using EnsureThat;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema.Model
{
    public class ParameterDefinition<T>
    {
        private readonly SqlDbType _type;
        private readonly bool _nullable;
        private readonly byte _precision;
        private readonly byte _scale;
        private readonly long _length;

        public ParameterDefinition(SqlDbType type, bool nullable)
        {
            _type = type;
            _nullable = nullable;
        }

        public ParameterDefinition(SqlDbType type, bool nullable, long length)
        {
            _type = type;
            _nullable = nullable;
            _length = length;
        }

        public ParameterDefinition(SqlDbType type, bool nullable, byte precision, byte scale)
        {
            _type = type;
            _nullable = nullable;
            _precision = precision;
            _scale = scale;
        }

        public virtual SqlParameter AddParameter(SqlParameterCollection parameters, T value, string parameterName)
        {
            EnsureArg.IsNotNull(parameters, nameof(parameters));

            return parameters.Add(
                new SqlParameter(
                    parameterName: parameterName,
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
