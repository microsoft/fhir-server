// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EnsureThat;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SemanticSearch
{
    internal static class SqlVectorFormatter
    {
        public static string Format(IReadOnlyList<float> embedding)
        {
            EnsureArg.IsNotNull(embedding, nameof(embedding));

            var builder = new StringBuilder((embedding.Count * 8) + 2);
            builder.Append('[');

            for (int index = 0; index < embedding.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append(embedding[index].ToString("R", CultureInfo.InvariantCulture));
            }

            builder.Append(']');
            return builder.ToString();
        }
    }
}
