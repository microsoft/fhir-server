// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Text;
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

        public ReferenceSqlParser(SearchParameterCollection parameterCollection, ISqlServerFhirModel fhirModel)
            : base(parameterCollection)
        {
            ArgumentNullException.ThrowIfNull(fhirModel);
            _fhirModel = fhirModel;
        }

        public override string BuildWhereClause(string value, string modifier, int? columnSuffix = null)
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

            var conditions = new StringBuilder();

            // Build WHERE conditions
            if (!string.IsNullOrEmpty(resourceId))
            {
                var escapedId = EscapeSqlValue(resourceId);
                conditions.Append($"t.ReferenceResourceId{suffix} = {escapedId}");
            }

            if (!string.IsNullOrEmpty(resourceType))
            {
                // Look up the resource type ID using the model
                if (_fhirModel.TryGetResourceTypeId(resourceType, out short resourceTypeId))
                {
                    if (conditions.Length > 0)
                    {
                        conditions.Append(" AND ");
                    }

                    conditions.Append($"t.ReferenceResourceTypeId{suffix} = {resourceTypeId}");
                }
                else
                {
                    // If the resource type is not found, the search should return no results
                    // This is handled by returning a condition that will never match
                    return "1=0";
                }
            }

            if (!string.IsNullOrEmpty(baseUri))
            {
                var escapedBaseUri = EscapeSqlValue(baseUri);
                if (conditions.Length > 0)
                {
                    conditions.Append(" AND ");
                }

                conditions.Append($"t.BaseUri{suffix} = {escapedBaseUri}");
            }

            return conditions.Length > 0 ? conditions.ToString() : "1=1";
        }
    }
}
