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
    internal partial class SqlService : SqlUtils.SqlService
    {
        internal int MergeResources(
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
                        IEnumerable<TokenStringCompositeSearchParam> tokenStringCompositeSearchParams)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("dbo.MergeResources", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 600 };

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

            var rows = new SqlParameter("@AffectedRows", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(rows);
            cmd.ExecuteNonQuery();
            return (int)rows.Value;
        }

        internal IEnumerable<T> GetData<T>(Func<SqlDataReader, T> toT, short resourceTypeId, long minSurId, long maxSurId)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand($"SELECT * FROM dbo.{typeof(T).Name} WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId BETWEEN @MinSurId AND @MaxSurId ORDER BY ResourceSurrogateId", conn) { CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@MinSurId", minSurId);
            cmd.Parameters.AddWithValue("@MaxSurId", maxSurId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                yield return toT(reader);
            }
        }
    }
}
