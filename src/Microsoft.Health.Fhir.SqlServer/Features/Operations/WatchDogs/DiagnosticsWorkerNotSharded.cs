// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using Microsoft.Health.Fhir.Store.SqlUtils;
using Microsoft.Health.Fhir.Store.Utils;

namespace Microsoft.Health.Fhir.Store.WatchDogs
{
    public class DiagnosticsWorkerNotSharded
    {
        public DiagnosticsWorkerNotSharded(string connStr)
        {
            SqlService = new SqlService(connStr);
            BatchExtensions.StartTask(() =>
            {
                Run();
            });
        }

        public SqlService SqlService { get; private set; }

        private void Run()
        {
            while (true)
            {
                retry:
                try
                {
                    LogDiagnostics();
                    Thread.Sleep(10000);
                }
                catch (Exception e)
                {
                    SqlService.LogEvent($"Diagnostics", "Error", string.Empty, text: e.ToString());
                    Thread.Sleep(10000);
                    goto retry;
                }
            }
        }

        private bool IsEnabled()
        {
            using var conn = SqlService.GetConnection();
            using var cmd = new SqlCommand("SELECT convert(bit,Number) FROM dbo.Parameters WHERE Id = 'Diagnostics.IsEnabled'", conn);
            var flag = cmd.ExecuteScalar();
            return flag != null && (bool)flag;
        }

        private void LogDiagnostics()
        {
            if (IsEnabled())
            {
                using var cmd = new SqlCommand(@"
INSERT INTO dbo.SG_PAGEIOLATCH
  SELECT wait_type, wait_resource, wait_time, Date=getUTCdate()
    --INTO SG_PAGEIOLATCH
    FROM sys.dm_exec_sessions S LEFT OUTER JOIN sys.dm_exec_requests R ON R.session_id = S.session_id
    WHERE S.session_id <> @@spid
      AND wait_type LIKE 'PAGEIOLATCH%'
      AND wait_time > 10000
  UNION ALL 
  SELECT 'KeepAlive','',0,getUTCdate()
                ");
                SqlService.ExecuteSqlWithRetries(SqlService.ConnectionString, cmd, cmdInt => cmdInt.ExecuteNonQuery());
            }
        }
    }
}
