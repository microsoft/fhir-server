// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal class SqlQueryHashCalculator : ISqlQueryHashCalculator
    {
        private const string NormalizedQueryShapePrefix = " fhir=";

        public string CalculateHash(string query)
        {
            return RemoveParametersHash(query).ComputeHash();
        }

        // This method negates effect of the AddParametersHash(). This is done this way to keep current SQL generator logic.
        internal static string RemoveParametersHash(string query)
        {
            var hashStartIndex = query.IndexOf(SqlQueryGenerator.ParametersHashStart, StringComparison.OrdinalIgnoreCase);
            if (hashStartIndex < 0) // no parameters hash
            {
                return query;
            }

            var hashEndIndex = query[hashStartIndex..].IndexOf(SqlQueryGenerator.ParametersHashEnd, StringComparison.OrdinalIgnoreCase);
            var hashLine = query[hashStartIndex..(hashStartIndex + hashEndIndex + SqlQueryGenerator.ParametersHashStart.Length)];
            return RemoveNormalizedQueryShapeAnnotations(query.Replace(hashLine, string.Empty, StringComparison.OrdinalIgnoreCase));
        }

        private static string RemoveNormalizedQueryShapeAnnotations(string query)
        {
            int searchIndex = 0;
            while ((searchIndex = query.IndexOf(SqlQueryGenerator.ParametersHashStart, searchIndex, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int contentStartIndex = searchIndex + SqlQueryGenerator.ParametersHashStart.Length;
                int nextHashStartIndex = query.IndexOf(
                    SqlQueryGenerator.ParametersHashStart,
                    contentStartIndex,
                    StringComparison.OrdinalIgnoreCase);
                int hashEndIndex = query.IndexOf(
                    SqlQueryGenerator.ParametersHashEnd,
                    contentStartIndex,
                    StringComparison.OrdinalIgnoreCase);
                bool hasTerminatorBeforeNextHash = hashEndIndex >= 0
                    && (nextHashStartIndex < 0 || hashEndIndex < nextHashStartIndex);
                int commentBoundaryIndex = hasTerminatorBeforeNextHash
                    ? hashEndIndex
                    : nextHashStartIndex >= 0
                        ? nextHashStartIndex
                        : query.Length;

                int annotationIndex = query.IndexOf(
                    NormalizedQueryShapePrefix,
                    contentStartIndex,
                    commentBoundaryIndex - contentStartIndex,
                    StringComparison.Ordinal);
                if (annotationIndex >= 0)
                {
                    int annotationEndIndex = annotationIndex + NormalizedQueryShapePrefix.Length;
                    while (annotationEndIndex < commentBoundaryIndex && IsNormalizedQueryShapeCharacter(query[annotationEndIndex]))
                    {
                        annotationEndIndex++;
                    }

                    query = query.Remove(annotationIndex, annotationEndIndex - annotationIndex);
                    searchIndex = annotationIndex;
                }
                else if (hasTerminatorBeforeNextHash)
                {
                    searchIndex = hashEndIndex + SqlQueryGenerator.ParametersHashEnd.Length;
                }
                else if (nextHashStartIndex >= 0)
                {
                    searchIndex = nextHashStartIndex;
                }
                else
                {
                    break;
                }
            }

            return query;
        }

        private static bool IsNormalizedQueryShapeCharacter(char character)
        {
            return (character >= 'A' && character <= 'Z')
                || (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character is '-' or '.' or '_' or ':' or '$' or '/' or '?' or '&' or '~';
        }
    }
}
