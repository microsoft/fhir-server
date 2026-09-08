// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Text;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    /// <summary>
    /// Parser for reference search parameters (e.g., subject, patient, performer).
    /// Reference parameters can be in various formats:
    /// - "Patient/123" (resource type and id)
    /// - "123" (just id, any resource type)
    /// - "http://example.org/Patient/123" (absolute URL)
    /// </summary>
    public class ReferenceSqlParser : BaseSqlParser
    {
        private readonly ISqlServerFhirModel _fhirModel;

        public ReferenceSqlParser(SqlSearchParameterDefinitionManager parameterCollection, ISqlServerFhirModel fhirModel)
            : base(parameterCollection)
        {
            ArgumentNullException.ThrowIfNull(fhirModel);
            _fhirModel = fhirModel;
            SetTableName("ReferenceSearchParam");
        }

        public override string BuildWhereClause(string value, string modifier, ParserOptions options, int? columnSuffix = null, string tableName = "t")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "1=1";
            }

            var suffix = columnSuffix.HasValue ? columnSuffix.Value.ToString() : string.Empty;

            // Parse the reference value
            // Formats:
            // - "ResourceType/id" - relative reference
            // - "id" - just the id
            // - "http://base/ResourceType/id" - absolute reference

            string? resourceType = null;
            string? resourceId = null;
            string? baseUri = null;

            // Check if it's an absolute URL
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Parse absolute URL: http://example.com/fhir/Patient/123
                var uri = new Uri(value);
                baseUri = $"{uri.Scheme}://{uri.Authority}";

                var pathParts = uri.AbsolutePath.TrimStart('/').Split('/');
                if (pathParts.Length >= 2)
                {
                    resourceType = pathParts[pathParts.Length - 2];
                    resourceId = pathParts[pathParts.Length - 1];
                }
                else if (pathParts.Length == 1)
                {
                    resourceId = pathParts[0];
                }
            }
            else if (value.Contains('/', StringComparison.Ordinal))
            {
                // Relative reference: ResourceType/id
                var parts = value.Split('/', 2);
                resourceType = parts[0];
                resourceId = parts[1];
            }
            else
            {
                // Just the resource ID
                resourceId = value;
            }

            // If a type modifier is provided (e.g., :Practitioner), use it when the value did not specify a type.
            if (!string.IsNullOrEmpty(modifier) && resourceType == null)
            {
                resourceType = modifier;
            }

            short resolvedResourceTypeId = 0;
            if (!string.IsNullOrEmpty(resourceType) &&
                !_fhirModel.TryGetResourceTypeId(resourceType, out resolvedResourceTypeId))
            {
                // If the resource type is not found, the search should return no results.
                return "1=0";
            }

            var conditions = new StringBuilder();

            // Build WHERE conditions
            if (!string.IsNullOrEmpty(resourceId))
            {
                var resourceIdParameter = options.AddParameter(VLatest.ReferenceSearchParam.ReferenceResourceId, resourceId, includeInHash: true);
                conditions.Append($"{tableName}.ReferenceResourceId{suffix} = {resourceIdParameter}");
            }

            if (!string.IsNullOrEmpty(resourceType))
            {
                if (conditions.Length > 0)
                {
                    conditions.Append(" AND ");
                }

                var resourceTypeIdValue = options.AddParameter(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, resolvedResourceTypeId, includeInHash: true);
                conditions.Append($"{tableName}.ReferenceResourceTypeId{suffix} = {resourceTypeIdValue}");
            }

            if (!string.IsNullOrEmpty(baseUri))
            {
                var baseUriParameter = options.AddParameter(VLatest.ReferenceSearchParam.BaseUri, baseUri, includeInHash: true);
                if (conditions.Length > 0)
                {
                    conditions.Append(" AND ");
                }

                conditions.Append($"{tableName}.BaseUri{suffix} = {baseUriParameter}");
            }

            return conditions.Length > 0 ? conditions.ToString() : "1=1";
        }
    }
}
