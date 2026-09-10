// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers
{
    internal class SearchParameterWrapper
    {
        private readonly Lazy<string> _url;
        private readonly Lazy<IReadOnlyList<ITypedElement>> _component;
        private readonly Lazy<string> _expression;
        private readonly Lazy<IReadOnlyList<string>> _base;
        private readonly Lazy<string> _name;
        private readonly Lazy<string> _code;
        private readonly Lazy<IReadOnlyList<string>> _target;
        private readonly Lazy<string> _description;
        private readonly Lazy<string> _status;
        private Lazy<string> _type;
        private readonly Lazy<VectorSearchParameterConfig> _vectorConfig;

        public SearchParameterWrapper(ITypedElement searchParameter)
        {
            EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
            EnsureArg.Is(KnownResourceTypes.SearchParameter, searchParameter.InstanceType, StringComparison.Ordinal, nameof(searchParameter));

            _name = new Lazy<string>(() => searchParameter.Scalar("name")?.ToString());
            _code = new Lazy<string>(() => searchParameter.Scalar("code")?.ToString());
            _description = new Lazy<string>(() => searchParameter.Scalar("description")?.ToString());
            _status = new Lazy<string>(() => searchParameter.Scalar("status")?.ToString());
            _url = new Lazy<string>(() => searchParameter.Scalar("url")?.ToString());
            _expression = new Lazy<string>(() => searchParameter.Scalar("expression")?.ToString());
            _type = new Lazy<string>(() => searchParameter.Scalar("type")?.ToString());

            _base = new Lazy<IReadOnlyList<string>>(() => searchParameter.Select("base")?.AsStringValues().ToArray());
            _component = new Lazy<IReadOnlyList<ITypedElement>>(() => searchParameter.Select("component")?.ToArray());
            _target = new Lazy<IReadOnlyList<string>>(() => searchParameter.Select("target")?.AsStringValues().ToArray());
            _vectorConfig = new Lazy<VectorSearchParameterConfig>(() => ParseVectorConfig(searchParameter.Select("extension")));
        }

        public string Name => _name.Value;

        public string Code => _code.Value;

        public string Description => _description.Value;

        public string Status => _status.Value;

#pragma warning disable CA1056 // URI-like properties should not be strings
        public string Url => _url.Value;
#pragma warning restore CA1056 // URI-like properties should not be strings

        public string Type => _type.Value;

        public string Expression => _expression.Value;

        public IReadOnlyList<string> Base => _base.Value;

        public IReadOnlyList<string> Target => _target.Value;

        public IReadOnlyList<ITypedElement> Component => _component.Value;

        public VectorSearchParameterConfig VectorConfig => _vectorConfig.Value;

        private static VectorSearchParameterConfig ParseVectorConfig(IEnumerable<ITypedElement> extensions)
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
                else if (string.Equals(url, VectorSearchParameterConfig.SourceStrategyExtensionUrl, StringComparison.Ordinal))
                {
                    if (!Enum.TryParse(value?.ToString(), ignoreCase: true, out VectorTextSourceStrategy sourceStrategy))
                    {
                        throw new InvalidDefinitionException($"Vector SearchParameter source strategy '{value}' is not supported.");
                    }

                    configuration.SourceStrategy = sourceStrategy;
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
                    if (!int.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int chunkSizeTokens) || chunkSizeTokens <= 0)
                    {
                        throw new InvalidDefinitionException("Vector SearchParameter chunkSizeTokens must be greater than zero.");
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
