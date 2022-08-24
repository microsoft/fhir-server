// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Data.SqlClient;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    public static class ShardingUtils
    {
#pragma warning disable CA1304 // Specify CultureInfo
        public static string GetCanonicalConnectionString(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            var serverDatabase = "server=" + builder.DataSource.ToLower() + ";database=" + builder.InitialCatalog.ToLower();
            return string.IsNullOrEmpty(builder.UserID)
                        ? serverDatabase + ";integrated security=true"
                        : serverDatabase + ";user=" + builder.UserID.ToLower() + ";pwd=" + builder.Password;
        }

        public static string GetServerDatabase(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            return "server=" + builder.DataSource.ToLower() + ";database=" + builder.InitialCatalog.ToLower();
        }
#pragma warning restore CA1304 // Specify CultureInfo

        public static string ReplaceConnectionStringSecurity(string connectionString, string user, string pwd)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (user != null && pwd != null)
            {
                builder.UserID = user;
                builder.Password = pwd;
            }

            return builder.ToString();
        }

        // Propagates secondary propertries of source connection to output
        public static string ReplicateConnectionStringProperties(string source, string target)
        {
            var sBuilder = new SqlConnectionStringBuilder(source);
            var tBuilder = new SqlConnectionStringBuilder(target);
            sBuilder.DataSource = tBuilder.DataSource;
            sBuilder.InitialCatalog = tBuilder.InitialCatalog;
            sBuilder.IntegratedSecurity = tBuilder.IntegratedSecurity;
            sBuilder.UserID = tBuilder.UserID;
            sBuilder.Password = tBuilder.Password;
            return sBuilder.ToString();
        }

        internal static void ParseConnectionString(string connectionString, out string server, out string database, out string user, out string pwd)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            server = builder.DataSource;
            database = builder.InitialCatalog;
            user = builder.IntegratedSecurity ? string.Empty : builder.UserID;
            pwd = builder.IntegratedSecurity ? string.Empty : builder.Password;
        }

        internal static string SetConnectionStringApplicationName(SqlConnectionStringBuilder builder, string user, string appName, bool isInternalReplica)
        {
            if (user != null)
            {
                if (appName != null)
                {
                    builder.ApplicationName = user + "." + appName;
                }
                else
                {
                    builder.ApplicationName = user;
                }
            }
            else
            {
                builder.ApplicationName = appName ?? "Default";
            }

            return isInternalReplica ? builder + ";applicationintent=readonly" : builder.ToString();
        }
    }
}
