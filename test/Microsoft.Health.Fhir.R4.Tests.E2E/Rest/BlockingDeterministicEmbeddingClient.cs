// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Tests.Common;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public sealed class BlockingDeterministicEmbeddingClient : IEmbeddingClient
    {
        private readonly DeterministicEmbeddingClient _inner = new DeterministicEmbeddingClient();
        private readonly object _syncLock = new object();
        private TaskCompletionSource _embeddingStarted;
        private TaskCompletionSource _releaseEmbedding;

        public int Dimensions => _inner.Dimensions;

        public Task WaitForBlockedEmbeddingAsync()
        {
            lock (_syncLock)
            {
                _embeddingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _releaseEmbedding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return _embeddingStarted.Task;
            }
        }

        public void ReleaseBlockedEmbedding()
        {
            lock (_syncLock)
            {
                _releaseEmbedding?.TrySetResult();
            }
        }

        public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            Task releaseTask = null;

            lock (_syncLock)
            {
                if (_embeddingStarted != null)
                {
                    _embeddingStarted.TrySetResult();
                    releaseTask = _releaseEmbedding.Task;
                    _embeddingStarted = null;
                }
            }

            if (releaseTask != null)
            {
                await releaseTask.WaitAsync(cancellationToken);
            }

            return await _inner.GenerateEmbeddingsAsync(texts, cancellationToken);
        }
    }
}
