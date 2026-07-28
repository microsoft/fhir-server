// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        private readonly IVectorResourceReader _resourceReader;
        private readonly IResourceDeserializer _resourceDeserializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="VectorTextSourceResolver"/> class.
        /// </summary>
        /// <param name="resourceReader">The persisted resource reader.</param>
        /// <param name="resourceDeserializer">The FHIR resource deserializer.</param>
        public VectorTextSourceResolver(IVectorResourceReader resourceReader, IResourceDeserializer resourceDeserializer)
        {
            _resourceReader = EnsureArg.IsNotNull(resourceReader, nameof(resourceReader));
            _resourceDeserializer = EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
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

                if (binary == null || binary.IsDeleted || binary.IsHistory || !TryDecodeBinary(binary, searchParameter.VectorConfig.MaxInputTokens, out string text))
                {
                    continue;
                }

                sources.Add(new VectorTextSource(text, BinaryResourceType, binary.ResourceId, binary.Version, BinaryDataPath));
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

        private bool TryDecodeBinary(ResourceWrapper binary, int maxInputTokens, out string text)
        {
            text = null;

            try
            {
                ResourceElement resource = _resourceDeserializer.Deserialize(binary);
                if (!string.Equals(resource.InstanceType, BinaryResourceType, StringComparison.Ordinal))
                {
                    return false;
                }

                string contentType = resource.Instance.Children("contentType").SingleOrDefault()?.Value?.ToString();
                if (!IsUtf8PlainText(contentType))
                {
                    return false;
                }

                object data = resource.Instance.Children("data").SingleOrDefault()?.Value;
                int maximumBytes = (int)Math.Min((long)maxInputTokens * MaximumUtf8BytesPerToken, int.MaxValue);
                if (!TryGetBytes(data, maximumBytes, out byte[] bytes))
                {
                    return false;
                }

                text = StrictUtf8.GetString(bytes);
                return !string.IsNullOrWhiteSpace(text);
            }
            catch (Exception exception) when (exception is FormatException or DecoderFallbackException or InvalidOperationException)
            {
                return false;
            }
        }

        private static bool IsUtf8PlainText(string contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return false;
            }

            string[] parts = contentType.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !string.Equals(parts[0], "text/plain", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string charset = parts
                .Skip(1)
                .FirstOrDefault(part => part.StartsWith("charset=", StringComparison.OrdinalIgnoreCase));

            return charset == null || string.Equals(charset.Substring("charset=".Length).Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase);
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
