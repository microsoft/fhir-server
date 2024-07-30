// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class ProcessingFlag<TLogger>
    {
        private readonly ILogger<TLogger> _logger;
        private bool _isEnabled;
        private DateTime? _lastUpdated;
        private readonly object _databaseAccessLocker = new object();
        private readonly string _parameterId;
        private readonly bool _defaultValue;

        public ProcessingFlag(string parameterId, bool defaultValue, ILogger<TLogger> logger)
        {
            _parameterId = parameterId;
            _defaultValue = defaultValue;
            _logger = logger;
        }

        public string ParameterId => _parameterId;

        public void Reset()
        {
            _lastUpdated = null;
        }

        public bool IsEnabled(ISqlRetryService sqlRetryService)
        {
            if (_lastUpdated.HasValue && (DateTime.UtcNow - _lastUpdated.Value).TotalSeconds < 600)
            {
                return _isEnabled;
            }

            lock (_databaseAccessLocker)
            {
                if (_lastUpdated.HasValue && (DateTime.UtcNow - _lastUpdated.Value).TotalSeconds < 600)
                {
                    return _isEnabled;
                }

                _isEnabled = IsEnabledInDatabase(sqlRetryService);
                _lastUpdated = DateTime.UtcNow;
            }

            return _isEnabled;
        }

        private bool IsEnabledInDatabase(ISqlRetryService sqlRetryService)
        {
            try
            {
                using var cmd = new SqlCommand();
                cmd.CommandText = "IF object_id('dbo.Parameters') IS NOT NULL SELECT Number FROM dbo.Parameters WHERE Id = @Id"; // call can be made before store is initialized
                cmd.Parameters.AddWithValue("@Id", _parameterId);
                var value = cmd.ExecuteScalarAsync(sqlRetryService, _logger, CancellationToken.None, disableRetries: true).Result;
                return value == null ? _defaultValue : (double)value == 1;
            }
            catch (Exception)
            {
                return _defaultValue;
            }
        }
    }
}
