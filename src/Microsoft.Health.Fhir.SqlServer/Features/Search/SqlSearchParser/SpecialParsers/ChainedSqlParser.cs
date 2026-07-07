// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class ChainedSqlParser : ISqlParser
    {
        private readonly SqlSearchParameterDefinitionManager _parameterCollection;
        private readonly SearchParameterSqlParser _parserSource;
        private readonly ISqlServerFhirModel _model;

        public ChainedSqlParser(SqlSearchParameterDefinitionManager parameterCollection, SearchParameterSqlParser parserSource, ISqlServerFhirModel model)
        {
            ArgumentNullException.ThrowIfNull(parameterCollection);
            ArgumentNullException.ThrowIfNull(parserSource);
            ArgumentNullException.ThrowIfNull(model);

            _parameterCollection = parameterCollection;
            _parserSource = parserSource;
            _model = model;
        }

        public string Parse(string name, string value, ParserOptions options)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            // Split the chained parameter name
            var parts = name.Split('.', 2);
            if (parts.Length < 2)
            {
                throw new ArgumentException("Invalid chained parameter format.", nameof(name));
            }

            var firstCode = parts[0];
            var remainingChain = parts[1];

            // Look up the first parameter (should be a reference parameter)
            var parameter = _parameterCollection.GetByCode(firstCode, options.ResourceTypes.FirstOrDefault());
            if (parameter == null)
            {
                throw new ArgumentException($"Search parameter '{firstCode}' is not supported for resource type '{options.ResourceTypes.FirstOrDefault()}'.", nameof(name));
            }

            var resourceTypeIds = parameter.SearchParameterInfo.TargetResourceTypes.Select(t => _model.GetResourceTypeId(t)).ToList();

            var sqlBuilder = new StringBuilder();
            var chainCteName = $"{options.CteName}chain{options.ChainLevel}_0";

            // Create the first CTE for the reference join
            if (!string.IsNullOrEmpty(options.LastCteName))
            {
                sqlBuilder.Append(',');
            }

            sqlBuilder.AppendLine($"{chainCteName} AS (");
            sqlBuilder.AppendLine("  SELECT DISTINCT refSource.ReferenceResourceTypeId AS RefResourceTypeId, refTarget.ResourceSurrogateId AS RefResourceSurrogateId, refSource.ResourceTypeId AS ResourceTypeId, refSource.ResourceSurrogateId AS ResourceSurrogateId");
            sqlBuilder.AppendLine("  FROM dbo.ReferenceSearchParam refSource");
            sqlBuilder.AppendLine("  JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId");

            sqlBuilder.AppendLine($"  JOIN {options.LastCteName ?? "dbo.Resource"} source ON source.{(options.ChainLevel > 0 ? "ref" : string.Empty)}ResourceSurrogateId = refSource.ResourceSurrogateId");

            sqlBuilder.AppendLine($"  WHERE refSource.SearchParamId = {parameter.Id}");
            sqlBuilder.AppendLine("  AND refTarget.IsHistory = 0");
            sqlBuilder.AppendLine("  AND refTarget.IsDeleted = 0");

            if (firstCode.Contains(':', StringComparison.OrdinalIgnoreCase))
            {
                var resourceType = firstCode.Split(':')[1];
                resourceTypeIds = new List<short> { _model.GetResourceTypeId(resourceType) };
                sqlBuilder.AppendLine($"  AND refSource.ReferenceResourceTypeId = {resourceTypeIds[0]}");
            }

            // Add base filters only on the first CTE
            if (options.LastCteName == null)
            {
                sqlBuilder.AppendLine($"  AND source.IsHistory = 0 AND source.IsDeleted = 0");

                if (options.ResourceTypes != null && options.ResourceTypes.Count > 0)
                {
                    var sourceResourceTypeIds = string.Join(", ", options.ResourceTypes);
                    sqlBuilder.AppendLine($"  AND source.ResourceTypeId IN ({sourceResourceTypeIds})");
                }

                if (options.ContinuationToken != null)
                {
                    sqlBuilder.AppendLine($"  AND source.ResourceSurrogateId {(options.SortDescending ? "<" : ">")} {options.ContinuationToken.ResourceSurrogateId}");

                    if (options.ContinuationToken.ResourceTypeId != null)
                    {
                        sqlBuilder.AppendLine($"  AND source.ResourceTypeId {(options.SortDescending ? "<" : ">")}= {options.ContinuationToken.ResourceTypeId}");
                    }
                }
            }

            if (!remainingChain.Equals(KnownQueryParameterNames.Type, StringComparison.OrdinalIgnoreCase))
            {
                sqlBuilder.AppendLine();
                sqlBuilder.AppendLine(")");

                // Recursively parse the remaining chain
                // Get the parameter for the remaining chain to find the right parser
                var remainingParameterParser = _parserSource.GetParser(remainingChain, resourceTypeIds[0]);

                var chainedSql = remainingParameterParser.Parse(
                    remainingChain,
                    value,
                    new ParserOptions
                    {
                        CteName = options.CteName,
                        LastCteName = chainCteName,
                        ChainLevel = options.ChainLevel + 1,
                        ResourceTypes = resourceTypeIds,
                    });

                if (chainedSql == null)
                {
                    throw new ArgumentException("Chained SQL parsing failed.", nameof(name));
                }

                chainCteName = $"{options.CteName}chain{options.ChainLevel}_1";

                if (!(remainingParameterParser is ChainedSqlParser))
                {
                    sqlBuilder.AppendLine($",{chainCteName} AS (");
                    sqlBuilder.Append(chainedSql);
                    sqlBuilder.AppendLine(")");
                }
                else
                {
                    sqlBuilder.Append(chainedSql);
                }
            }
            else
            {
                var valueTypeIds = value.Split(',').Select(v => _model.GetResourceTypeId(v)).ToList();
                sqlBuilder.AppendLine($"  AND refTarget.ResourceTypeId IN ({string.Join(",", valueTypeIds)})");
                sqlBuilder.AppendLine(")");
            }

            if (options.ChainLevel == 0)
            {
                sqlBuilder.AppendLine($",{options.CteName} AS (")
                    .AppendLine("  SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, 1 AS IsMatch, 0 AS IsPartial, row_number() OVER (ORDER BY ResourceTypeId ASC, ResourceSurrogateId ASC) AS Row")
                    .AppendLine($"  FROM {chainCteName}")
                    .AppendLine(")");
            }
            else
            {
                var parentCteName = $"{options.CteName}chain{options.ChainLevel - 1}";
                sqlBuilder.AppendLine($",{parentCteName}_1 AS (")
                    .AppendLine("  SELECT DISTINCT parent.ResourceTypeId, parent.ResourceSurrogateId, 1 AS IsMatch, 0 AS IsPartial, row_number() OVER (ORDER BY parent.ResourceTypeId ASC, parent.ResourceSurrogateId ASC) AS Row")
                    .AppendLine($"  FROM {chainCteName} child")
                    .AppendLine($"    JOIN {parentCteName}_0 parent ON parent.RefResourceTypeId = child.ResourceTypeId AND parent.RefResourceSurrogateId = child.ResourceSurrogateId")
                    .AppendLine(")");
            }

            return sqlBuilder.ToString();
        }
    }
}
