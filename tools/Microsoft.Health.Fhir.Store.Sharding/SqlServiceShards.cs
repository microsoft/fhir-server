// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    /// <summary>
    /// Handles all communication between the API and SQL Server.
    /// </summary>
    public partial class SqlService
    {
        public void PutShardTransaction(TransactionId transactionId)
        {
            ParallelForEachShard(
                (shardId) =>
                {
                    using var cmd = new SqlCommand("dbo.PutShardTransaction") { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
                    cmd.Parameters.AddWithValue("@TransactionId", transactionId.Id);
                    ExecuteSqlWithRetries(shardId, cmd, c => c.ExecuteNonQuery(), 60);
                },
                null);
        }

        private Dictionary<ShardId, List<T>> ShardList<T>(IEnumerable<T> objects, Func<T, ShardletId> getShardletId)
        {
            // result shold be full list of shards with null values for empty lists
            var shardedObjects = new Dictionary<ShardId, List<T>>();
            foreach (var shardId in ShardletMap.Shards.Keys)
            {
                shardedObjects.Add(shardId, null);
            }

            if (objects != null)
            {
                foreach (var obj in objects)
                {
                    var shardId = ShardletMap.ShardletShards[getShardletId(obj)];
                    var resInt = shardedObjects[shardId];
                    if (resInt != null)
                    {
                        resInt.Add(obj);
                    }
                    else
                    {
                        shardedObjects[shardId] = new List<T>() { obj };
                    }
                }
            }

            return shardedObjects;
        }

        public int MergeResources(
            TransactionId transactionId,
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
            // shard lists
            var resourcesSharded = ShardList(resources, res => res.ShardletId);
            var referenceSearchParamsSharded = ShardList(referenceSearchParams, res => res.ShardletId);
            var tokenSearchParamsSharded = ShardList(tokenSearchParams, res => res.ShardletId);
            var compartmentAssignmentsSharded = ShardList(compartmentAssignments, res => res.ShardletId);
            var tokenTextsSharded = ShardList(tokenTexts, res => res.ShardletId);
            var dateTimeSearchParamsSharded = ShardList(dateTimeSearchParams, res => res.ShardletId);
            var tokenQuantityCompositeSearchParamsSharded = ShardList(tokenQuantityCompositeSearchParams, res => res.ShardletId);
            var quantitySearchParamsSharded = ShardList(quantitySearchParams, res => res.ShardletId);
            var stringSearchParamsSharded = ShardList(stringSearchParams, res => res.ShardletId);
            var tokenTokenCompositeSearchParamsSharded = ShardList(tokenTokenCompositeSearchParams, res => res.ShardletId);
            var tokenStringCompositeSearchParamsSharded = ShardList(tokenStringCompositeSearchParams, res => res.ShardletId);

            var affectedRows = ShardletMap.Shards.ToDictionary(_ => _.Key, _ => 0);
            ParallelForEachShard(
                (shardId) =>
                {
                    if (resourcesSharded[shardId] != null)
                    {
                        // transaction is recorded inside merge stored procedure to avoid extra call to the database
                        affectedRows[shardId] = MergeResourcesSingleShard(
                                                    shardId,
                                                    resourcesSharded[shardId],
                                                    referenceSearchParamsSharded[shardId],
                                                    tokenSearchParamsSharded[shardId],
                                                    compartmentAssignmentsSharded[shardId],
                                                    tokenTextsSharded[shardId],
                                                    dateTimeSearchParamsSharded[shardId],
                                                    tokenQuantityCompositeSearchParamsSharded[shardId],
                                                    quantitySearchParamsSharded[shardId],
                                                    stringSearchParamsSharded[shardId],
                                                    tokenTokenCompositeSearchParamsSharded[shardId],
                                                    tokenStringCompositeSearchParamsSharded[shardId]);
                    }
                    else
                    {
                        PutShardTransaction(transactionId);
                    }
                },
                null);

            return affectedRows.Sum(_ => _.Value);
        }

        private int MergeResourcesSingleShard(
            ShardId shardId,
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
            using var cmd = new SqlCommand("dbo.MergeResources") { CommandType = CommandType.StoredProcedure, CommandTimeout = 3600 };

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

            ExecuteSqlWithRetries(shardId, cmd, c => c.ExecuteNonQuery());

            return (int)rows.Value;
        }
    }
}
