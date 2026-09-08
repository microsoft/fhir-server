// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    internal static class SqlSearchParameterHashAppender
    {
        public static void Append(SqlQueryBuilder builder, HashingSqlQueryParameterManager manager, bool reuseQueryPlans)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(manager);

            if (reuseQueryPlans || !manager.HasParametersToHash)
            {
                return;
            }

            var hashBuilder = new IndentedStringBuilder(new StringBuilder());
            manager.AppendHash(hashBuilder);
            manager.AppendHashedParameterNames(hashBuilder);

            builder.DecreaseIndent(builder.IndentLevel)
                .Append(SqlSearchConstants.ParametersHashStart)
                .Append(hashBuilder.ToString())
                .Append(SqlSearchConstants.ParametersHashEnd);
        }
    }
}
