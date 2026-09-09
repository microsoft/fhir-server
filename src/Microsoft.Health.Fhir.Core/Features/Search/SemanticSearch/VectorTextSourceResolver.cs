// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Resolves direct text and local Binary references selected by vector SearchParameters.
    /// </summary>
    public sealed class VectorTextSourceResolver : IVectorTextSourceResolver
    {
        private const string BinaryResourceType = "Binary";
        private const string BinaryDataPath = "Binary.data";
        private const int MaximumUtf8BytesPerToken = 4;

        private readonly IVectorResourceReader _resourceReader;
        private readonly IResourceDeserializer _resourceDeserializer;
        private readonly Dictionary<string, IBinaryContentExtractor> _binaryContentExtractors;

        /// <summary>
        /// Initializes a new instance of the <see cref="VectorTextSourceResolver"/> class.
        /// </summary>
        /// <param name="resourceReader">The persisted resource reader.</param>
        /// <param name="resourceDeserializer">The FHIR resource deserializer.</param>
        /// <param name="binaryContentExtractors">The registered Binary content extractors.</param>
        public VectorTextSourceResolver(
            IVectorResourceReader resourceReader,
            IResourceDeserializer resourceDeserializer,
            IEnumerable<IBinaryContentExtractor> binaryContentExtractors)
        {
            _resourceReader = EnsureArg.IsNotNull(resourceReader, nameof(resourceReader));
            _resourceDeserializer = EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
            _binaryContentExtractors = EnsureArg.IsNotNull(binaryContentExtractors, nameof(binaryContentExtractors))
                .SelectMany(extractor => extractor.SupportedContentTypes.Select(contentType => (ContentType: contentType, Extractor: extractor)))
                .ToDictionary(item => item.ContentType, item => item.Extractor, StringComparer.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<VectorTextSource>> ResolveAsync(
            ResourceWrapper owner,
            SearchParameterInfo searchParameter,
            IReadOnlyList<string> extractedValues,
            IReadOnlyCollection<ResourceWrapper> writeBatch,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(owner, nameof(owner));
            EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
            EnsureArg.IsNotNull(extractedValues, nameof(extractedValues));
            EnsureArg.IsNotNull(writeBatch, nameof(writeBatch));

            if (searchParameter.VectorConfig.SourceStrategy == VectorTextSourceStrategy.DirectText)
            {
                return extractedValues
                    .Select(value => new VectorTextSource(value, owner.ResourceTypeName, owner.ResourceId, owner.Version, searchParameter.Expression))
                    .ToList();
            }

            if (searchParameter.VectorConfig.SourceStrategy != VectorTextSourceStrategy.LocalBinaryReference)
            {
                throw new InvalidOperationException($"Unsupported vector text source strategy '{searchParameter.VectorConfig.SourceStrategy}'.");
            }

            var sources = new List<VectorTextSource>();
            foreach (string extractedValue in extractedValues)
            {
                if (!TryParseBinaryReference(extractedValue, out string binaryId))
                {
                    continue;
                }

                ResourceWrapper binary = writeBatch
                    .Reverse()
                    .FirstOrDefault(resource =>
                        string.Equals(resource.ResourceTypeName, BinaryResourceType, StringComparison.Ordinal) &&
                        string.Equals(resource.ResourceId, binaryId, StringComparison.Ordinal));

                if (binary == null)
                {
                    binary = await _resourceReader.GetAsync(new ResourceKey(BinaryResourceType, binaryId), cancellationToken);
                }

                if (binary == null || binary.IsDeleted || binary.IsHistory || !TryDecodeBinary(binary, searchParameter.VectorConfig.MaxInputTokens, out IReadOnlyList<BinaryContentSegment> segments))
                {
                    continue;
                }

                sources.AddRange(segments.Select(segment => new VectorTextSource(
                    segment.Text,
                    BinaryResourceType,
                    binary.ResourceId,
                    binary.Version,
                    GetSourcePath(segment.SourceLocator))));
            }

            return sources;
        }

        private static bool TryParseBinaryReference(string value, out string binaryId)
        {
            binaryId = null;
            if (string.IsNullOrWhiteSpace(value) || Uri.TryCreate(value, UriKind.Absolute, out _))
            {
                return false;
            }

            string[] segments = value.Split('/');
            if (segments.Length != 2 ||
                !string.Equals(segments[0], BinaryResourceType, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(segments[1]) ||
                segments[1].Contains('?', StringComparison.Ordinal) ||
                segments[1].Contains('#', StringComparison.Ordinal))
            {
                return false;
            }

            binaryId = segments[1];
            return true;
        }

        private bool TryDecodeBinary(ResourceWrapper binary, int maxInputTokens, out IReadOnlyList<BinaryContentSegment> segments)
        {
            segments = null;

            try
            {
                ResourceElement resource = _resourceDeserializer.Deserialize(binary);
                if (!string.Equals(resource.InstanceType, BinaryResourceType, StringComparison.Ordinal))
                {
                    return false;
                }

                string contentType = resource.Instance.Children("contentType").SingleOrDefault()?.Value?.ToString();
                string normalizedContentType = contentType?.Split(';', 2, StringSplitOptions.TrimEntries)[0];
                if (string.IsNullOrWhiteSpace(normalizedContentType) || !_binaryContentExtractors.TryGetValue(normalizedContentType, out IBinaryContentExtractor extractor))
                {
                    return false;
                }

                object data = resource.Instance.Children("data").SingleOrDefault()?.Value;
                int maximumTextLength = (int)Math.Min((long)maxInputTokens * MaximumUtf8BytesPerToken, int.MaxValue);
                int maximumBytes = extractor.GetMaximumContentLength(maximumTextLength);
                if (!TryGetBytes(data, maximumBytes, out byte[] bytes))
                {
                    return false;
                }

                return extractor.TryExtract(bytes, contentType, maximumTextLength, out segments) && segments?.Count > 0;
            }
            catch (Exception exception) when (exception is FormatException or InvalidOperationException)
            {
                return false;
            }
        }

        private static string GetSourcePath(string sourceLocator)
        {
            return string.IsNullOrWhiteSpace(sourceLocator) ? BinaryDataPath : $"{BinaryDataPath}#{sourceLocator}";
        }

        private static bool TryGetBytes(object data, int maximumBytes, out byte[] bytes)
        {
            bytes = null;
            if (data is byte[] binaryData)
            {
                if (binaryData.Length > maximumBytes)
                {
                    return false;
                }

                bytes = binaryData;
                return true;
            }

            if (data is not string encoded || encoded.Length > (((long)maximumBytes + 2) / 3 * 4) + 4)
            {
                return false;
            }

            bytes = Convert.FromBase64String(encoded);
            return bytes.Length <= maximumBytes;
        }
    }
}
