// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    internal static class VectorSearchParameterExtensions
    {
        /// <summary>
        /// Parses vector search configuration from a SearchParameter's extensions.
        /// </summary>
        /// <param name="extensions">The SearchParameter extension elements.</param>
        /// <returns>The vector configuration, or null when no vector configuration extension is present.</returns>
        /// <exception cref="InvalidDefinitionException">The vector configuration is duplicated or invalid.</exception>
        public static VectorSearchParameterConfig ParseVectorConfig(this IEnumerable<ITypedElement> extensions)
        {
            ITypedElement[] vectorExtensions = extensions
                .Where(extension => string.Equals(
                    extension.Scalar("url")?.ToString(),
                    VectorSearchParameterConfig.ExtensionUrl,
                    StringComparison.Ordinal))
                .ToArray();

            if (vectorExtensions.Length == 0)
            {
                return null;
            }

            if (vectorExtensions.Length > 1)
            {
                throw new InvalidDefinitionException($"SearchParameter contains multiple '{VectorSearchParameterConfig.ExtensionUrl}' extensions.");
            }

            var configuration = new VectorSearchParameterConfig();
            foreach (ITypedElement nestedExtension in vectorExtensions[0].Select("extension"))
            {
                string url = nestedExtension.Scalar("url")?.ToString();
                object value = nestedExtension.Scalar("value") ??
                    nestedExtension.Scalar("valueCode") ??
                    nestedExtension.Scalar("valueInteger") ??
                    nestedExtension.Scalar("valueDecimal");

                if (string.Equals(url, VectorSearchParameterConfig.ExtractionPolicyExtensionUrl, StringComparison.Ordinal))
                {
                    if (!Enum.TryParse(value?.ToString(), ignoreCase: true, out VectorTextExtractionPolicy extractionPolicy))
                    {
                        throw new InvalidDefinitionException($"Vector SearchParameter extraction policy '{value}' is not supported.");
                    }

                    configuration.ExtractionPolicy = extractionPolicy;
                }
                else if (string.Equals(url, VectorSearchParameterConfig.MaxInputTokensExtensionUrl, StringComparison.Ordinal))
                {
                    if (!int.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxInputTokens) || maxInputTokens <= 0)
                    {
                        throw new InvalidDefinitionException("Vector SearchParameter maxInputTokens must be greater than zero.");
                    }

                    configuration.MaxInputTokens = maxInputTokens;
                }
                else if (string.Equals(url, VectorSearchParameterConfig.MinimumScoreExtensionUrl, StringComparison.Ordinal))
                {
                    if (!decimal.TryParse(value?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal minimumScore) || minimumScore < 0 || minimumScore > 1)
                    {
                        throw new InvalidDefinitionException("Vector SearchParameter minimumScore must be between zero and one.");
                    }

                    configuration.MinimumScore = minimumScore;
                }
                else if (string.Equals(url, VectorSearchParameterConfig.ChunkSizeTokensExtensionUrl, StringComparison.Ordinal))
                {
                    if (!int.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int chunkSizeTokens) ||
                        chunkSizeTokens < VectorSearchConfiguration.MinimumChunkSizeTokens ||
                        chunkSizeTokens > VectorSearchConfiguration.MaxEmbeddingInputTokens)
                    {
                        throw new InvalidDefinitionException($"Vector SearchParameter chunkSizeTokens must be between {VectorSearchConfiguration.MinimumChunkSizeTokens} and {VectorSearchConfiguration.MaxEmbeddingInputTokens}.");
                    }

                    configuration.ChunkSizeTokens = chunkSizeTokens;
                }
                else if (string.Equals(url, VectorSearchParameterConfig.ChunkOverlapTokensExtensionUrl, StringComparison.Ordinal))
                {
                    if (!int.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int chunkOverlapTokens) || chunkOverlapTokens < 0)
                    {
                        throw new InvalidDefinitionException("Vector SearchParameter chunkOverlapTokens must be non-negative.");
                    }

                    configuration.ChunkOverlapTokens = chunkOverlapTokens;
                }
                else if (string.Equals(url, VectorSearchParameterConfig.DistanceMetricExtensionUrl, StringComparison.Ordinal))
                {
                    string distanceMetric = value?.ToString();
                    if (!string.Equals(distanceMetric, VectorSearchConfiguration.SupportedDistanceMetric, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDefinitionException($"Vector SearchParameter distanceMetric must be '{VectorSearchConfiguration.SupportedDistanceMetric}'.");
                    }

                    configuration.DistanceMetric = distanceMetric.ToLowerInvariant();
                }
                else
                {
                    throw new InvalidDefinitionException($"Vector SearchParameter setting '{url}' is not supported.");
                }
            }

            if (configuration.ChunkSizeTokens.HasValue &&
                configuration.ChunkOverlapTokens >= configuration.ChunkSizeTokens)
            {
                throw new InvalidDefinitionException("Vector SearchParameter chunkOverlapTokens must be smaller than chunkSizeTokens.");
            }

            return configuration;
        }
    }
}
