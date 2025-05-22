// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Storage;
using JobStatus = Microsoft.Health.JobManagement.JobStatus;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public static class JobInfoExtensions
    {
        public static async Task<JobInfo> ReadJobInfoAsync(this SqlDataReader sqlDataReader, CancellationToken cancellationToken)
        {
            if (await sqlDataReader.ReadAsync(cancellationToken))
            {
                return LoadJobInfo(sqlDataReader);
            }

            return null;
        }

        public static async Task<IReadOnlyList<JobInfo>> ReadJobInfosAsync(this SqlDataReader sqlDataReader, CancellationToken cancellationToken)
        {
            List<JobInfo> outcome = new List<JobInfo>();
            while (await sqlDataReader.ReadAsync(cancellationToken))
            {
                JobInfo jobInfo = LoadJobInfo(sqlDataReader);
                outcome.Add(jobInfo);
            }

            return outcome;
        }

        public static JobInfo LoadJobInfo(SqlDataReader sqlDataReader)
        {
            EnsureArg.IsNotNull(sqlDataReader, nameof(sqlDataReader));

            try
            {
                var jobQueueTable = VLatest.JobQueue;
                var jobInfo = new JobInfo();

                // Helper function to safely get column ordinal
                int? GetOrdinal(string columnName)
                {
                    for (int i = 0; i < sqlDataReader.FieldCount; i++)
                    {
                        if (string.Equals(sqlDataReader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
                        {
                            return i;
                        }
                    }

                    return null;
                }

                // Helper function to safely convert numeric types
                long GetInt64Value(int ordinal)
                {
                    if (sqlDataReader.IsDBNull(ordinal))
                    {
                        return 0;
                    }

                    return sqlDataReader.GetFieldType(ordinal).Name switch
                    {
                        "Byte" => sqlDataReader.GetByte(ordinal),
                        "Int16" => sqlDataReader.GetInt16(ordinal),
                        "Int32" => sqlDataReader.GetInt32(ordinal),
                        "Int64" => sqlDataReader.GetInt64(ordinal),
                        _ => 0,
                    };
                }

                // Helper function to safely read nullable value
                T? ReadNullable<T>(string columnName, Func<int, T> reader)
                    where T : struct
                {
                    var ordinal = GetOrdinal(columnName);
                    return ordinal.HasValue && !sqlDataReader.IsDBNull(ordinal.Value) ? reader(ordinal.Value) : null;
                }

                // Required field - JobId
                var jobIdOrdinal = GetOrdinal("JobId");
                if (!jobIdOrdinal.HasValue)
                {
                    throw new InvalidOperationException("Required column 'JobId' was not found in the result set.");
                }

                jobInfo.Id = GetInt64Value(jobIdOrdinal.Value);

                // All other fields are optional
                var queueTypeOrdinal = GetOrdinal("QueueType");
                jobInfo.QueueType = queueTypeOrdinal.HasValue ? sqlDataReader.GetByte(queueTypeOrdinal.Value) : (byte)0;

                var statusOrdinal = GetOrdinal(jobQueueTable.Status.Metadata.Name);
                jobInfo.Status = statusOrdinal.HasValue && !sqlDataReader.IsDBNull(statusOrdinal.Value)
                    ? (JobStatus)sqlDataReader.GetByte(statusOrdinal.Value)
                    : null;

                jobInfo.GroupId = ReadNullable(jobQueueTable.GroupId.Metadata.Name, ordinal => GetInt64Value(ordinal)) ?? 0;
                jobInfo.Version = ReadNullable(jobQueueTable.Version.Metadata.Name, ordinal => GetInt64Value(ordinal)) ?? 0;
                jobInfo.Priority = ReadNullable(jobQueueTable.Priority.Metadata.Name, ordinal => GetInt64Value(ordinal)) ?? 0;
                jobInfo.Data = ReadNullable(jobQueueTable.Data.Metadata.Name, ordinal => GetInt64Value(ordinal));

                var resultOrdinal = GetOrdinal(jobQueueTable.Result.Metadata.Name);
                jobInfo.Result = resultOrdinal.HasValue && !sqlDataReader.IsDBNull(resultOrdinal.Value)
                    ? sqlDataReader.GetString(resultOrdinal.Value)
                    : null;

                var definitionOrdinal = GetOrdinal(jobQueueTable.Definition.Metadata.Name);
                jobInfo.Definition = definitionOrdinal.HasValue && !sqlDataReader.IsDBNull(definitionOrdinal.Value)
                    ? sqlDataReader.GetString(definitionOrdinal.Value)
                    : null;

                var createDateOrdinal = GetOrdinal(jobQueueTable.CreateDate.Metadata.Name);
                jobInfo.CreateDate = createDateOrdinal.HasValue && !sqlDataReader.IsDBNull(createDateOrdinal.Value)
                    ? sqlDataReader.GetDateTime(createDateOrdinal.Value)
                    : DateTime.UtcNow;

                var heartbeatDateOrdinal = GetOrdinal(jobQueueTable.HeartbeatDate.Metadata.Name);
                jobInfo.HeartbeatDateTime = heartbeatDateOrdinal.HasValue && !sqlDataReader.IsDBNull(heartbeatDateOrdinal.Value)
                    ? sqlDataReader.GetDateTime(heartbeatDateOrdinal.Value)
                    : DateTime.UtcNow;

                var cancelRequestedOrdinal = GetOrdinal(jobQueueTable.CancelRequested.Metadata.Name);
                jobInfo.CancelRequested = cancelRequestedOrdinal.HasValue && !sqlDataReader.IsDBNull(cancelRequestedOrdinal.Value)
                    ? sqlDataReader.GetBoolean(cancelRequestedOrdinal.Value)
                    : false;

                jobInfo.StartDate = ReadNullable(jobQueueTable.StartDate.Metadata.Name, ordinal => sqlDataReader.GetDateTime(ordinal));
                jobInfo.EndDate = ReadNullable(jobQueueTable.EndDate.Metadata.Name, ordinal => sqlDataReader.GetDateTime(ordinal));

                return jobInfo;
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException ||
                                     ex is InvalidOperationException ||
                                     ex is InvalidCastException)
            {
                var columnInfo = new List<string>();
                for (int i = 0; i < sqlDataReader.FieldCount; i++)
                {
                    columnInfo.Add($"{sqlDataReader.GetName(i)} ({sqlDataReader.GetFieldType(i).Name})");
                }

                throw new InvalidOperationException(
                    $"Failed to read job info from SQL result set. Available columns and types: {string.Join(", ", columnInfo)}",
                    ex);
            }
        }
    }
}
