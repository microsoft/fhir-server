// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace Microsoft.Health.Fhir.Store.Copy
{
    public partial class SqlService : SqlUtils.SqlService
    {
        public int InsertResources(
                        bool isMerge,
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
            using var cmd = new SqlCommand(isMerge ? "dbo.MergeResources" : "dbo.InsertResources", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 3600 };

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

            if (isMerge)
            {
                cmd.Parameters.AddWithValue("@SingleTransaction", singleTransaction);
            }

            var rows = new SqlParameter("@AffectedRows", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(rows);
            cmd.ExecuteNonQuery();
            return (int)rows.Value;
        }

        internal IEnumerable<T> GetData<T>(Func<SqlDataReader, T> toT, short resourceTypeId, string minId, string maxId, bool convertToLong)
        {
            return convertToLong ? GetData(toT, resourceTypeId, long.Parse(minId), long.Parse(maxId)) : GetData(toT, resourceTypeId, minId, maxId);
        }

        private IEnumerable<T> GetData<T>(Func<SqlDataReader, T> toT, short resourceTypeId, long minId, long maxId)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                @$"
SELECT * FROM dbo.{typeof(T).Name} WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId BETWEEN @MinId AND @MaxId ORDER BY ResourceSurrogateId",
                conn) { CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@MinId", minId);
            cmd.Parameters.AddWithValue("@MaxId", maxId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                yield return toT(reader);
            }
        }

        private IEnumerable<T> GetData<T>(Func<SqlDataReader, T> toT, short resourceTypeId, string minId, string maxId)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                @$"
DECLARE @DummyTop bigint = 9223372036854775807
SELECT * 
  FROM dbo.{typeof(T).Name} WITH (INDEX = 1)
  WHERE ResourceTypeId = @ResourceTypeId
    AND ResourceSurrogateId IN (SELECT TOP (@DummyTop) ResourceSurrogateId FROM dbo.Resource WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId) WHERE ResourceTypeId = @ResourceTypeId AND ResourceId BETWEEN @MinId AND @MaxId AND IsHistory = 0)
  ORDER BY ResourceSurrogateId
  OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))",
                conn) { CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@MinId", minId);
            cmd.Parameters.AddWithValue("@MaxId", maxId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                yield return toT(reader);
            }
        }

        internal IEnumerable<T> GetData<T>(Func<SqlDataReader, T> toT, short resourceTypeId, IEnumerable<string> resourceIds, bool useSecondaryStore)
        {
            using var conn = new SqlConnection(GetTrueConnectionString(useSecondaryStore));
            conn.Open();
            using var cmd = new SqlCommand(
                @$"
DECLARE @DummyTop bigint = 9223372036854775807
SELECT * 
  FROM dbo.{typeof(T).Name} WITH (INDEX = 1)
  WHERE ResourceTypeId = @ResourceTypeId
    AND ResourceSurrogateId IN 
          (SELECT TOP (@DummyTop) ResourceSurrogateId 
             FROM dbo.Resource WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId) 
             WHERE ResourceTypeId = @ResourceTypeId 
               AND ResourceId IN (SELECT TOP (@DummyTop) String FROM @ResourceIds)
               AND IsHistory = 0
          )
  OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))",
                conn)
            { CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            var resourceIdsParam = new SqlParameter { ParameterName = "@ResourceIds" };
            resourceIdsParam.AddStringList(resourceIds);
            cmd.Parameters.Add(resourceIdsParam);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                yield return toT(reader);
            }
        }
    }
}
