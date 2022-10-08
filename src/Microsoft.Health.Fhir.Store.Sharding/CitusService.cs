// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Npgsql;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    public class CitusService
    {
        private readonly string _connectionString;

        public CitusService(string connectionString)
        {
            _connectionString = connectionString;
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
            IEnumerable<TokenStringCompositeSearchParam> tokenStringCompositeSearchParams)
        {
            int c = 0;

            using (var connection = new NpgsqlConnection(_connectionString))
            {
                connection.Open();

                c += BulkLoadTable(connection, resources, "resource", CitusResourceExtension.WriteRow);
                c += BulkLoadTable(connection, referenceSearchParams, "referencesearchparam", CitusReferenceSearchParamExtension.WriteRow);
                c += BulkLoadTable(connection, tokenSearchParams, "tokensearchparam", CitusTokenSearchParamExtension.WriteRow);
                c += BulkLoadTable(connection, compartmentAssignments, "compartmentassignment", CitusCompartmentAssignmentExtension.WriteRow);
                c += BulkLoadTable(connection, tokenTexts, "tokentext", CitusTokenTextExtension.WriteRow);
                c += BulkLoadTable(connection, dateTimeSearchParams, "datetimesearchparam", CitusDateTimeSearchParamExtension.WriteRow);
                c += BulkLoadTable(connection, tokenQuantityCompositeSearchParams, "tokenquantitycompositesearchparam", CitusTokenQuantityCompositeSearchParamExtension.WriteRow);
                c += BulkLoadTable(connection, quantitySearchParams, "quantitysearchparam", CitusQuantitySearchParamExtension.WriteRow);
                c += BulkLoadTable(connection, stringSearchParams, "stringsearchparam", CitusStringSearchParamExtension.WriteRow);
                c += BulkLoadTable(connection, tokenTokenCompositeSearchParams, "tokentokencompositesearchparam", CitusTokenTokenCompositeSearchParamExtension.WriteRow);
                c += BulkLoadTable(connection, tokenStringCompositeSearchParams, "tokenstringcompositesearchparam", CitusTokenStringCompositeSearchParamExtension.WriteRow);
            }

            return c;
        }

        private static int BulkLoadTable<T>(
            Npgsql.NpgsqlConnection connection,
            IEnumerable<T> rows,
            string tableName,
            Action<NpgsqlBinaryImporter, T> writeRow)
        {
            int c = 0;

            if (rows != null)
            {
                using (var writer = connection.BeginBinaryImport($"COPY {tableName} FROM STDIN (FORMAT BINARY)"))
                {
                    // this timeout should govern the below writer.Complete call (which defaults to 30s)
                    writer.Timeout = TimeSpan.FromMinutes(5);

                    foreach (var row in rows)
                    {
                        writer.StartRow();
                        writeRow(writer, row);
                        c++;
                    }

                    Polly.Policy
                        .Handle<System.TimeoutException>()
                        .Retry(2, CitusService.OnRetry)
                        .Execute(writer.Complete);
                }
            }

            return c;
        }

        private static void OnRetry(Exception e, int count)
        {
            // TODO: add logging?
        }
    }
}
