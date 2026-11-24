// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    /// <summary>
    /// The custom query procedures of one partition, keyed by query hash.
    /// </summary>
    public sealed class CustomQueryProcedures
    {
        private const string Prefix = "CustomQuery_";

        private readonly FrozenDictionary<string, string> _procedureNamesByHash;

        private CustomQueryProcedures(FrozenDictionary<string, string> procedureNamesByHash)
        {
            _procedureNamesByHash = procedureNamesByHash;
        }

        /// <summary>
        /// Gets the procedure for a query hash.
        /// </summary>
        /// <param name="queryHash">The query hash.</param>
        /// <param name="procedureName">The procedure name when found; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> if a procedure exists for the hash.</returns>
        public bool TryGetProcedureName(string queryHash, out string procedureName) =>
            _procedureNamesByHash.TryGetValue(EnsureArg.IsNotNull(queryHash, nameof(queryHash)), out procedureName);

        /// <summary>
        /// Builds the lookup from procedure names. Names that aren't exactly <c>CustomQuery_&lt;hash&gt;</c> are ignored.
        /// </summary>
        /// <param name="procedureNames">The procedure names.</param>
        /// <returns>The procedures keyed by hash.</returns>
        internal static CustomQueryProcedures Create(IEnumerable<string> procedureNames)
        {
            var procedureNamesByHash = new Dictionary<string, string>();
            foreach (string name in EnsureArg.IsNotNull(procedureNames, nameof(procedureNames)))
            {
                string[] tokens = name.Split('_');
                if (tokens.Length == 2 && name.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    procedureNamesByHash.TryAdd(tokens[1], name);
                }
            }

            return new CustomQueryProcedures(procedureNamesByHash.ToFrozenDictionary());
        }
    }
}
