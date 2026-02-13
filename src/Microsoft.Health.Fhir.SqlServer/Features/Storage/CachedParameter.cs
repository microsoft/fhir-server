// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Reads a numeric parameter from dbo.Parameters and caches it with a 10-minute TTL.
    /// </summary>
    /// <typeparam name="TLogger">The type used for logging category.</typeparam>
    public class CachedParameter<TLogger>
    {
        private readonly ILogger<TLogger> _logger;
        private readonly string _parameterId;
        private readonly double _defaultValue;
        private readonly object _databaseAccessLocker = new object();
        private double _cachedValue;
        private DateTime? _lastUpdated;

        public CachedParameter(string parameterId, double defaultValue, ILogger<TLogger> logger)
        {
            _parameterId = parameterId;
            _defaultValue = defaultValue;
            _cachedValue = defaultValue;
            _logger = logger;
        }

        public string ParameterId => _parameterId;

        public void Reset()
        {
            _lastUpdated = null;
        }

        public double GetValue(ISqlRetryService sqlRetryService)
        {
            if (_lastUpdated.HasValue && (DateTime.UtcNow - _lastUpdated.Value).TotalSeconds < 600)
            {
                return _cachedValue;
            }

            lock (_databaseAccessLocker)
            {
                if (_lastUpdated.HasValue && (DateTime.UtcNow - _lastUpdated.Value).TotalSeconds < 600)
                {
                    return _cachedValue;
                }

                _cachedValue = ReadFromDatabase(sqlRetryService);
                _lastUpdated = DateTime.UtcNow;
            }

            return _cachedValue;
        }

        private double ReadFromDatabase(ISqlRetryService sqlRetryService)
        {
            try
            {
                using var cmd = new SqlCommand();
                cmd.CommandText = "IF object_id('dbo.Parameters') IS NOT NULL SELECT Number FROM dbo.Parameters WHERE Id = @Id";
                cmd.Parameters.AddWithValue("@Id", _parameterId);
                var value = cmd.ExecuteScalarAsync(sqlRetryService, _logger, CancellationToken.None, disableRetries: true).Result;
                if (value == null || Convert.IsDBNull(value))
                {
                    return _defaultValue;
                }

                return Convert.ToDouble(value);
            }
            catch (Exception)
            {
                return _defaultValue;
            }
        }
    }
}
