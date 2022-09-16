// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Core.Extensions;
using Npgsql;
using NpgsqlTypes;

namespace Microsoft.Health.Fhir.PostgresQL
{
    internal static class PostgresQLQuery
    {
        private static string connectionString = "Host=localhost;Port=5432;Username=postgres;password=;Database=";
        private const int ShiftFactor = 3;

        internal static readonly DateTime MaxDateTime = new DateTime(long.MaxValue >> ShiftFactor, DateTimeKind.Utc).TruncateToMillisecond().AddTicks(-1);

        public static long LastUpdatedToResourceSurrogateId(DateTime dateTime)
        {
            EnsureArg.IsLte(dateTime, MaxDateTime, nameof(dateTime));
            long id = dateTime.TruncateToMillisecond().Ticks << ShiftFactor;

            return id;
        }

        internal static string? GetData()
        {
            string? result = null;
            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                using (var command = new NpgsqlCommand("select * from Resource", conn))
                {
                    var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        result = reader.GetInt16(0).ToString();
                    }
                }
            }

            return result;
        }

        public static async Task<(long resourceSurrogateId, int version, Stream? rawResource)> ReadData()
        {
            using (var conn = new NpgsqlConnection(connectionString))
            {
                _ = conn.OpenAsync();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"select * from readresource((@restypeid), (@resid), (@vers))";
                    cmd.Parameters.Add(new NpgsqlParameter("restypeid", NpgsqlDbType.Smallint) { Value = 103 });
                    cmd.Parameters.Add(new NpgsqlParameter("resid", NpgsqlDbType.Varchar) { Value = "4038902" });
                    cmd.Parameters.Add(new NpgsqlParameter("vers", NpgsqlDbType.Integer) { Value = 23 });
                    var reader = await cmd.ExecuteReaderAsync();
                    long resourceSurrogateId = 0;
                    int version = 0;
                    bool isDeleted = false;
                    bool isHistory = false;
                    Stream? rawResourceStream = null;
                    bool isRawResourceMetaSet = false;
                    string? searchParamHash = null;

                    while (await reader.ReadAsync())
                    {
                        resourceSurrogateId = reader.GetInt64(0);
                        version = reader.GetInt32(1);
                        isDeleted = reader.GetBoolean(2);
                        isHistory = reader.GetBoolean(3);
                        rawResourceStream = reader.GetStream(4);
                        isRawResourceMetaSet = reader.GetBoolean(5);
                        searchParamHash = reader.GetString(6);
                    }

                    return (resourceSurrogateId, version, rawResourceStream);
                }
            }
        }

        internal static void UpsertData()
        {
            long resourceSurrogateId = 758297;

            // NpgsqlConnection.GlobalTypeMapper.MapComposite<BulkResourceWriteClaimTableTypeV1Row>("BulkResourceWriteClaimTableType_1");
            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                conn.TypeMapper.MapComposite<BulkResourceWriteClaimTableTypeV1Row>("bulkresourcewriteclaimtabletype_1");
                conn.TypeMapper.MapComposite<BulkTokenTextTableTypeV1Row>("bulktokentexttabletype_2");
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"call upsertresource_3((@baseresourcesurrogateid)," +
                           $"(@restypeid)," +
                           $"(@resid), " +
                           $"(@etag), " +
                           $"(@allowcreate), " +
                           $"(@isdeleted), " +
                           $"(@keephistory), " +
                           $"(@requireetagonupdate), " +
                           $"(@requestmethod), " +
                           $"(@searchparamhash), " +
                           $"(@rawresource), " +
                           $"(@resourcewriteclaims), " +
                           $"(@tokentextsearchparams), " +
                           $"(@isresourcechangecaptureenabled), " +
                           $"(@comparedversion))";
                    cmd.Parameters.Add(new NpgsqlParameter("baseresourcesurrogateid", NpgsqlDbType.Bigint) { Value = resourceSurrogateId });
                    cmd.Parameters.Add(new NpgsqlParameter("restypeid", NpgsqlDbType.Smallint) { Value = (short)103 });
                    cmd.Parameters.Add(new NpgsqlParameter("resid", NpgsqlDbType.Varchar) { Value = "753829719" });
                    cmd.Parameters.Add(new NpgsqlParameter("etag", NpgsqlDbType.Integer) { Value = 12 });
                    cmd.Parameters.Add(new NpgsqlParameter("allowcreate", NpgsqlDbType.Bit) { Value = true });
                    cmd.Parameters.Add(new NpgsqlParameter("isdeleted", NpgsqlDbType.Bit) { Value = false });
                    cmd.Parameters.Add(new NpgsqlParameter("keephistory", NpgsqlDbType.Bit) { Value = false });
                    cmd.Parameters.Add(new NpgsqlParameter("requireetagonupdate", NpgsqlDbType.Bit) { Value = false });
                    cmd.Parameters.Add(new NpgsqlParameter("requestmethod", NpgsqlDbType.Varchar) { Value = "put" });
                    cmd.Parameters.Add(new NpgsqlParameter("searchparamhash", NpgsqlDbType.Varchar) { Value = "t89rewhgf8r9439" });
                    cmd.Parameters.Add(new NpgsqlParameter("rawresource", NpgsqlDbType.Bytea) { Value = new byte[] { 0x01, 0x02 } });
                    cmd.Parameters.Add(new NpgsqlParameter
                    {
                        ParameterName = "resourcewriteclaims",
                        Value = new BulkResourceWriteClaimTableTypeV1Row()
                        {
                            Offset = 0,
                            claimtypeid = 12,
                            Claimvalue = "78490ewhgiod",
                        },
                    });
                    cmd.Parameters.Add(new NpgsqlParameter
                    {
                        ParameterName = "tokentextsearchparams",
                        Value = new BulkTokenTextTableTypeV1Row()
                        {
                            offsetid = 0,
                            searchparamid = 12,
                            text = "549wjgdsk",
                        },
                    });
                    cmd.Parameters.Add(new NpgsqlParameter("isresourcechangecaptureenabled", NpgsqlDbType.Bit) { Value = false });
                    cmd.Parameters.Add(new NpgsqlParameter("comparedversion", NpgsqlDbType.Integer) { Value = 342 });

                    _ = cmd.ExecuteNonQuery();
                }

                conn.Close();
            }
        }

        internal class BulkResourceWriteClaimTableTypeV1Row
        {
            public BulkResourceWriteClaimTableTypeV1Row()
            {
            }

            public int Offset { get; set; }

#pragma warning disable SA1300 // Element should begin with upper-case letter
            public int claimtypeid { get; set; }

            public string? Claimvalue { get; set; }
#pragma warning restore SA1300 // Element should begin with upper-case letter
        }

        internal class BulkTokenTextTableTypeV1Row
        {
            public BulkTokenTextTableTypeV1Row()
            {
            }

#pragma warning disable SA1300 // Element should begin with upper-case letter
            public int offsetid { get; set; }

            public int searchparamid { get; set; }

            public string? text { get; set; }
#pragma warning restore SA1300 // Element should begin with upper-case letter

        }
    }
}
