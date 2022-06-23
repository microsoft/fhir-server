// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    public class ShardletMap
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public ShardletMap(string connectionString, int version)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            SetCentralStoreConnectionStringProperties(connectionString);
            Init(version);
        }

        public ReadOnlyDictionary<ShardletId, ShardId> ShardletShards { get; private set; }

        public ReadOnlyDictionary<ShardId, Shard> Shards { get; private set; }

        private string CentralStoreConnectionString { get; set; }

        private string CentralStoreUserId { get; set; }

        private string CentralStorePassword { get; set; }

        private bool CentralStoreIntegratedSecurity { get; set; }

        private string CentralStoreServerDatabase { get; set; }

        public Tuple<ReadOnlyDictionary<ShardletId, ShardId>, ReadOnlyDictionary<ShardId, Shard>> GetShardsInfo()
        {
            return Tuple.Create(ShardletShards, Shards);
        }

        private void SetCentralStoreConnectionStringProperties(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            var finalBuilder = new SqlConnectionStringBuilder { DataSource = builder.DataSource, InitialCatalog = builder.InitialCatalog, UserID = builder.UserID, Password = builder.Password, IntegratedSecurity = builder.IntegratedSecurity };
            CentralStoreConnectionString = finalBuilder.ToString();
            CentralStoreIntegratedSecurity = finalBuilder.IntegratedSecurity;
            CentralStoreUserId = finalBuilder.UserID;
            CentralStorePassword = finalBuilder.Password;
            CentralStoreServerDatabase = $"server={builder.DataSource};database={builder.InitialCatalog}";
        }

        private string ApplyCentralSecurityToShardConnectionString(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (CentralStoreIntegratedSecurity)
            {
                return $"server={builder.DataSource};database={builder.InitialCatalog};untegrated security=true";
            }
            else if (string.IsNullOrEmpty(builder.UserID))
            {
                return $"server={builder.DataSource};database={builder.InitialCatalog};user={CentralStoreUserId};pwd={CentralStorePassword}";
            }
            else
            {
                throw new InvalidOperationException();
            }
        }

        private void Init(int version)
        {
         retry:
            try
            {
                var shardletIds = new List<ShardletId>();
                var shardletShards = new Dictionary<ShardletId, ShardId>();
                var shards = new Dictionary<ShardId, Shard>();

                using (var connection = new SqlConnection(CentralStoreConnectionString))
                {
                    connection.Open();
                    using (var command = new SqlCommand("dbo.GetShardlets", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@Version", version);

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var shardId = new ShardId(reader.GetByte(0));
                                var connectionString = reader.GetString(1);
                                connectionString = ApplyCentralSecurityToShardConnectionString(connectionString);
                                var shardletId = new ShardletId(reader.GetByte(2));
                                var versionInt = reader.GetInt32(3);
                                shardletIds.Add(shardletId);
                                shardletShards.Add(shardletId, shardId);
                                if (!shards.ContainsKey(shardId))
                                {
                                    shards.Add(shardId, new Shard(shardId, connectionString, versionInt));
                                }
                            }
                        }
                    }
                }

                if (shardletIds.Count == 0)
                {
                    throw new ArgumentException("No shardlet ids returned.");
                }

                if (shardletShards.Where(_ => shardletIds.Contains(_.Key)).Select(_ => _.Value).Distinct().Count() < shards.Count)
                {
                    throw new ArgumentException("Not all shards are covered by shardlet ids");
                }

                var readOnlyShardletShards = new ReadOnlyDictionary<ShardletId, ShardId>(shardletShards);
                var readOnlyShards = new ReadOnlyDictionary<ShardId, Shard>(shards);

                ShardletShards = readOnlyShardletShards;
                Shards = readOnlyShards;

                return;
            }
            catch (Exception e)
            {
                if (IsRetryable(e.ToString()))
                {
                    Thread.Sleep(2000);
                    goto retry;
                }

                throw;
            }
        }

        private static bool IsRetryable(string str)
        {
            return (str.Contains("unable to access database", StringComparison.OrdinalIgnoreCase) && str.Contains("high availability", StringComparison.OrdinalIgnoreCase))
                    || (str.Contains("timeout", StringComparison.OrdinalIgnoreCase) && str.Contains("connection from the pool", StringComparison.OrdinalIgnoreCase))
                    || str.Contains("internal .net framework data provider error 6.", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("is not currently available", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("database is in emergency mode", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("semaphore timeout", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("transport-level error", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("severe error occurred", StringComparison.OrdinalIgnoreCase);
        }
    }
}
