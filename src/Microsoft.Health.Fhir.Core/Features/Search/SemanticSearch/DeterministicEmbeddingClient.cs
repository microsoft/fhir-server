// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// An <see cref="IEmbeddingClient"/> that produces deterministic, L2-normalized vectors seeded from a hash
    /// of the input text. The vectors are reproducible but not semantically meaningful; this client exists so
    /// the write and read paths can be exercised offline in tests without calling an external model.
    /// </summary>
    public sealed class DeterministicEmbeddingClient : IEmbeddingClient
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeterministicEmbeddingClient"/> class.
        /// </summary>
        /// <param name="dimensions">The number of dimensions each embedding should have.</param>
        public DeterministicEmbeddingClient(int dimensions = VectorSearchConfiguration.SupportedDimensions)
        {
            EnsureArg.IsGt(dimensions, 0, nameof(dimensions));

            Dimensions = dimensions;
        }

        /// <inheritdoc />
        public int Dimensions { get; }

        /// <inheritdoc />
        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(texts, nameof(texts));

            var embeddings = new List<float[]>(texts.Count);

            foreach (string text in texts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                embeddings.Add(Embed(text ?? string.Empty));
            }

            return Task.FromResult<IReadOnlyList<float[]>>(embeddings);
        }

        private float[] Embed(string text)
        {
            // Derive the vector from a deterministic SHA-256 stream seeded by the text, so identical text always
            // yields the identical vector. A hash chain (rather than System.Random) keeps this reproducible and
            // avoids a security-sensitive PRNG for what is purely a test fixture.
            var vector = new float[Dimensions];
            double sumOfSquares = 0;

            byte[] block = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            int produced = 0;

            while (produced < Dimensions)
            {
                for (int offset = 0; offset + sizeof(uint) <= block.Length && produced < Dimensions; offset += sizeof(uint))
                {
                    uint sample = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(offset, sizeof(uint)));
                    float component = (float)(((double)sample / uint.MaxValue * 2) - 1);
                    vector[produced++] = component;
                    sumOfSquares += component * (double)component;
                }

                if (produced < Dimensions)
                {
                    // Extend the deterministic byte stream when one hash block is not enough.
                    block = SHA256.HashData(block);
                }
            }

            // L2-normalize so cosine distance behaves the way it does for real embeddings.
            double magnitude = Math.Sqrt(sumOfSquares);

            if (magnitude > 0)
            {
                for (int i = 0; i < Dimensions; i++)
                {
                    vector[i] = (float)(vector[i] / magnitude);
                }
            }

            return vector;
        }
    }
}
