// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;

namespace Microsoft.Health.Fhir.Store.Shards
{
    public class Shard
    {
        public Shard(ShardId shardId, string connectionString, int version)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            Id = shardId;
            ConnectionString = connectionString;
            Version = version;

            Utilities.ParseConnectionString(connectionString, out string server, out string database, out string user, out string pwd);
            Server = server;
            Database = database;
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="shardId">Unique new Shard Id</param>
        /// <param name="shard">Shard to be cloned from</param>
        public Shard(ShardId shardId, Shard shard)
        {
            Id = shardId;
            ConnectionString = shard.ConnectionString;
            Version = shard.Version;
            Server = shard.Server;
            Database = shard.Database;
        }

        public ShardId Id { get; }

        public string ConnectionString { get; }

        public int Version { get; }

        // The following are cached but can be derived from the ConnectionString
        public string Server { get; }

        public string Database { get; }
    }
}
