// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.CompositeParsers
{
    /// <summary>
    /// Parser for composite search parameters that combine a Token parameter with a Reference parameter.
    /// Example: Observation.code-subject combining code (token) and subject (reference).
    /// Format: "code|system$Patient/123" where the first part is a token and the second is a reference.
    /// Reference format: [ResourceType]/[id] or just [id] (e.g., "Patient/123" or "123").
    /// </summary>
    public class TokenReferenceCompositeSqlParser : BaseCompositeSqlParser
    {
        public TokenReferenceCompositeSqlParser(
            SearchParameterCollection parameterCollection,
            ISqlServerFhirModel fhirModel)
            : base(parameterCollection, new TokenSqlParser(parameterCollection), new ReferenceSqlParser(parameterCollection, fhirModel))
        {
        }
    }
}
