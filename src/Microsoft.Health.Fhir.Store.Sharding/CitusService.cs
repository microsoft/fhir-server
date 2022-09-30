// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Npgsql;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    public class CitusService
    {
        private string _connectionString;

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

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                c += resources.BulkLoadTable(conn);
            }

            return c;
        }
    }
}
