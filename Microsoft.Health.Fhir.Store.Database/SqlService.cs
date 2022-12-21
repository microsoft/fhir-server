// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities

using System.Data;
using System.Data.SqlClient;

namespace Microsoft.Health.Fhir.Store.Database
{
    public class SqlService : SqlUtils.SqlService
    {
        public SqlService(string connectionString)
            : base(connectionString, null)
        {
        }

        public int MergeResources(
                        IEnumerable<Resource> resources,
                        IEnumerable<ReferenceSearchParam> referenceSearchParams,
                        IEnumerable<TokenSearchParam> tokenSearchParams,
                        IEnumerable<CompartmentAssignment> compartmentAssignments,
                        IEnumerable<TokenText> tokenTexts,
                        IEnumerable<DateTimeSearchParam> dateTimeSearchParams,
                        IEnumerable<TokenQuantityCompositeSearchParam> tokenQuantityCompositeSearchParams,
                        IEnumerable<QuantitySearchParam> quantitySearchParams,
                        IEnumerable<StringSearchParam> stringSearchParams,
                        IEnumerable<TokenTokenCompositeSearchParam> tokenTokenCompositeSearchParams,
                        IEnumerable<TokenStringCompositeSearchParam> tokenStringCompositeSearchParams,
                        bool singleTransaction)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("dbo.MergeResources", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 3600 };

            var resourcesParam = new SqlParameter { ParameterName = "@Resources" };
            resourcesParam.AddResourceList(resources);
            cmd.Parameters.Add(resourcesParam);

            var referenceSearchParamsParam = new SqlParameter { ParameterName = "@ReferenceSearchParams" };
            referenceSearchParamsParam.AddReferenceSearchParamList(referenceSearchParams);
            cmd.Parameters.Add(referenceSearchParamsParam);

            var tokenSearchParamsParam = new SqlParameter { ParameterName = "@TokenSearchParams" };
            tokenSearchParamsParam.AddTokenSearchParamList(tokenSearchParams);
            cmd.Parameters.Add(tokenSearchParamsParam);

            var compartmentAssignmentsParam = new SqlParameter { ParameterName = "@CompartmentAssignments" };
            compartmentAssignmentsParam.AddCompartmentAssignmentList(compartmentAssignments);
            cmd.Parameters.Add(compartmentAssignmentsParam);

            var tokenTextsParam = new SqlParameter { ParameterName = "@TokenTexts" };
            tokenTextsParam.AddTokenTextList(tokenTexts);
            cmd.Parameters.Add(tokenTextsParam);

            var dateTimeSearchParamsParam = new SqlParameter { ParameterName = "@DateTimeSearchParams" };
            dateTimeSearchParamsParam.AddDateTimeSearchParamList(dateTimeSearchParams);
            cmd.Parameters.Add(dateTimeSearchParamsParam);

            var tokenQuantityCompositeSearchParamsParam = new SqlParameter { ParameterName = "@TokenQuantityCompositeSearchParams" };
            tokenQuantityCompositeSearchParamsParam.AddTokenQuantityCompositeSearchParamList(tokenQuantityCompositeSearchParams);
            cmd.Parameters.Add(tokenQuantityCompositeSearchParamsParam);

            var quantitySearchParamsParam = new SqlParameter { ParameterName = "@QuantitySearchParams" };
            quantitySearchParamsParam.AddQuantitySearchParamList(quantitySearchParams);
            cmd.Parameters.Add(quantitySearchParamsParam);

            var stringSearchParamsParam = new SqlParameter { ParameterName = "@StringSearchParams" };
            stringSearchParamsParam.AddStringSearchParamList(stringSearchParams);
            cmd.Parameters.Add(stringSearchParamsParam);

            var tokenTokenCompositeSearchParamsParam = new SqlParameter { ParameterName = "@TokenTokenCompositeSearchParams" };
            tokenTokenCompositeSearchParamsParam.AddTokenTokenCompositeSearchParamList(tokenTokenCompositeSearchParams);
            cmd.Parameters.Add(tokenTokenCompositeSearchParamsParam);

            var tokenStringCompositeSearchParamsParam = new SqlParameter { ParameterName = "@TokenStringCompositeSearchParams" };
            tokenStringCompositeSearchParamsParam.AddTokenStringCompositeSearchParamList(tokenStringCompositeSearchParams);
            cmd.Parameters.Add(tokenStringCompositeSearchParamsParam);

            cmd.Parameters.AddWithValue("@SingleTransaction", singleTransaction);

            var rows = new SqlParameter("@AffectedRows", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(rows);
            cmd.ExecuteNonQuery();
            return (int)rows.Value;
        }

        public IList<T> GetData<T>(Func<SqlDataReader, T> toT, short resourceTypeId, long minId, long maxId)
        {
            using var cmd = new SqlCommand($"SELECT * FROM dbo.{typeof(T).Name} WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId BETWEEN @MinId AND @MaxId ORDER BY ResourceSurrogateId") { CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@MinId", minId);
            cmd.Parameters.AddWithValue("@MaxId", maxId);
            return ExecuteSqlReaderWithRetries(cmd, reader => toT(reader));
        }

        public (long JobId, string Definition, long Version) DequeueJob(byte queueType)
        {
            using var command = new SqlCommand("dbo.DequeueJob") { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", queueType);
            command.Parameters.AddWithValue("@Worker", $"{Environment.MachineName}.{Environment.ProcessId}");
            command.Parameters.AddWithValue("@HeartbeatTimeoutSec", 600);
            var results = ExecuteSqlReaderWithRetries(command, reader =>
            {
                return (reader.GetInt64(1), reader.GetString(2), reader.GetInt64(3));
            });

            return results.Count > 0 ? (results[0].Item1, results[0].Item2, results[0].Item3) : (-1L, string.Empty, 0L);
        }

        public void CompleteJob(byte queueType, long jobId, bool failed, long version, int? resourceCount = null, int? totalCount = null)
        {
            using var command = new SqlCommand("dbo.PutJobStatus") { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@QueueType", queueType);
            command.Parameters.AddWithValue("@JobId", jobId);
            command.Parameters.AddWithValue("@Version", version);
            command.Parameters.AddWithValue("@Failed", failed);
            command.Parameters.AddWithValue("@RequestCancellationOnFailure", true);
            if (resourceCount.HasValue)
            {
                command.Parameters.AddWithValue("@Data", resourceCount.Value);
            }
            else
            {
                command.Parameters.AddWithValue("@Data", DBNull.Value);
            }

            if (totalCount.HasValue)
            {
                command.Parameters.AddWithValue("@FinalResult", $"total={totalCount.Value}");
            }
            else
            {
                command.Parameters.AddWithValue("@FinalResult", DBNull.Value);
            }

            ExecuteSqlWithRetries(command, cmd => cmd.ExecuteNonQuery());
        }
    }
}
