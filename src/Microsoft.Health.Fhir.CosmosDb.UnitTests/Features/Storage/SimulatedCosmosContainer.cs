// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage
{
    /// <summary>
    /// A stand-in for a Cosmos DB container holding a single document. The document is kept as JSON and is
    /// exchanged with the code under test through <see cref="FhirCosmosClientInitializer.FhirCosmosSerializer"/>,
    /// the very serializer the Cosmos DB client is configured with in production. That makes the round-trip
    /// semantics a test observes - including any document field the serializer cannot read back - identical to
    /// the ones the data store gets from the real service, which a hand-built mock returning the same object
    /// reference would silently hide.
    /// </summary>
    /// <remarks>
    /// Writes model the service faithfully in the way that matters for optimistic concurrency: a replace
    /// re-persists whatever the client serialized, a patch is applied server side to the stored JSON, both
    /// honour <c>If-Match</c> by failing with <see cref="HttpStatusCode.PreconditionFailed"/>, and every
    /// successful write assigns a new <c>_etag</c>.
    /// </remarks>
    internal sealed class SimulatedCosmosContainer
    {
        private static readonly CosmosSerializer DocumentSerializer =
            new FhirCosmosClientInitializer.FhirCosmosSerializer(NullLogger<FhirCosmosClientInitializer>.Instance);

        /// <summary>
        /// The number of write requests this simulation will accept before it declares the code under test to
        /// be looping. Any bounded retry policy gives up well below this, so crossing it is a failure rather
        /// than something to let spin.
        /// </summary>
        private const int WriteAttemptSafetyLimit = 25;

        private JObject _storedDocument;
        private JObject _snapshotForNextRead;
        private int _etagSequence;

        public SimulatedCosmosContainer(Container container)
        {
            EnsureArg.IsNotNull(container, nameof(container));

            container.ReadItemAsync<FhirCosmosResourceWrapper>(
                    Arg.Any<string>(),
                    Arg.Any<PartitionKey>(),
                    Arg.Any<ItemRequestOptions>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(ReadItem()));

            container.ReplaceItemAsync(
                    Arg.Any<FhirCosmosResourceWrapper>(),
                    Arg.Any<string>(),
                    Arg.Any<PartitionKey?>(),
                    Arg.Any<ItemRequestOptions>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo => Task.FromResult(ReplaceItem(
                    callInfo.ArgAt<FhirCosmosResourceWrapper>(0),
                    callInfo.ArgAt<ItemRequestOptions>(3))));

            container.PatchItemAsync<FhirCosmosResourceWrapper>(
                    Arg.Any<string>(),
                    Arg.Any<PartitionKey>(),
                    Arg.Any<IReadOnlyList<PatchOperation>>(),
                    Arg.Any<PatchItemRequestOptions>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo => Task.FromResult(PatchItem(
                    callInfo.ArgAt<IReadOnlyList<PatchOperation>>(2),
                    callInfo.ArgAt<PatchItemRequestOptions>(3))));

            container.CreateItemAsync(
                    Arg.Any<FhirCosmosResourceWrapper>(),
                    Arg.Any<PartitionKey?>(),
                    Arg.Any<ItemRequestOptions>(),
                    Arg.Any<CancellationToken>())
                .Returns<ItemResponse<FhirCosmosResourceWrapper>>(_ => throw CreateCosmosException(HttpStatusCode.Conflict));
        }

        /// <summary>
        /// Gets the number of writes that have reached the stored document.
        /// </summary>
        public int WriteCount { get; private set; }

        /// <summary>
        /// Gets the number of write requests the code under test has issued, including the ones the service
        /// rejected.
        /// </summary>
        public int WriteAttemptCount { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether another client writes to the stored document immediately
        /// before every write request reaches it, re-assigning the <c>_etag</c> while leaving the resource
        /// version alone. That is what a competing no-op confirming its own precondition does, and it means a
        /// conditional write can never win while a re-read keeps finding the version its guard expects - an
        /// unbounded loop for any caller that retries such a conflict indefinitely.
        /// </summary>
        public bool AConcurrentWriterWinsEveryRace { get; set; }

        /// <summary>
        /// Gets a copy of the stored document exactly as it is persisted, including the fields the wrapper
        /// type cannot deserialize.
        /// </summary>
        public JObject StoredDocument => (JObject)_storedDocument?.DeepClone();

        /// <summary>
        /// Gets the <c>_etag</c> currently assigned to the stored document.
        /// </summary>
        public string StoredETag => (string)_storedDocument?[KnownDocumentProperties.ETag];

        /// <summary>
        /// Persists <paramref name="wrapper"/> as the stored document, as a create through the data store would.
        /// </summary>
        /// <param name="wrapper">The document to store.</param>
        /// <param name="versionedByETag">
        /// When <c>true</c>, the stored document carries no <c>version</c> property, which is how a resource
        /// that has never been updated is persisted. Its FHIR version is then derived from its <c>_etag</c>.
        /// </param>
        public void StoreDocument(FhirCosmosResourceWrapper wrapper, bool versionedByETag = false)
        {
            EnsureArg.IsNotNull(wrapper, nameof(wrapper));

            JObject document = Serialize(wrapper);

            if (versionedByETag)
            {
                document.Remove(KnownResourceWrapperProperties.Version);
            }

            document[KnownDocumentProperties.ETag] = NextETag();
            _storedDocument = document;
        }

        /// <summary>
        /// Deserializes the stored document the way the data store's point read does.
        /// </summary>
        public FhirCosmosResourceWrapper ReadStoredDocument() => Deserialize(_storedDocument);

        /// <summary>
        /// Makes the next read return the document as it looks right now, even if the stored document moves on
        /// in the meantime. This reproduces a read served by a replica that has not yet caught up with another
        /// client's write - the exact situation an authoritative compare-and-swap has to detect.
        /// </summary>
        public void ServeNextReadFromASnapshot() => _snapshotForNextRead = (JObject)_storedDocument.DeepClone();

        /// <summary>
        /// Removes the stored document, as a concurrent hard delete would.
        /// </summary>
        public void SimulateConcurrentHardDelete() => _storedDocument = null;

        /// <summary>
        /// Strips the <c>_etag</c> from the stored document, leaving a document that offers nothing to make a
        /// conditional write out of.
        /// </summary>
        public void SimulateADocumentWithoutAnETag() => _storedDocument.Remove(KnownDocumentProperties.ETag);

        /// <summary>
        /// Applies an out-of-band change to the stored document, as a concurrent writer would.
        /// </summary>
        /// <param name="change">The change to apply to the stored JSON.</param>
        public void SimulateConcurrentWrite(Action<JObject> change)
        {
            EnsureArg.IsNotNull(change, nameof(change));

            change(_storedDocument);
            _storedDocument[KnownDocumentProperties.ETag] = NextETag();
        }

        private static CosmosException CreateCosmosException(HttpStatusCode statusCode) =>
            new CosmosException(statusCode.ToString(), statusCode, subStatusCode: 0, activityId: Guid.NewGuid().ToString(), requestCharge: 1);

        private static JObject Serialize(FhirCosmosResourceWrapper wrapper)
        {
            using Stream stream = DocumentSerializer.ToStream(wrapper);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return JObject.Parse(reader.ReadToEnd());
        }

        private static FhirCosmosResourceWrapper Deserialize(JObject document)
        {
            return DocumentSerializer.FromStream<FhirCosmosResourceWrapper>(
                new MemoryStream(Encoding.UTF8.GetBytes(document.ToString(Formatting.None))));
        }

        private static JToken ValueOf(PatchOperation operation)
        {
            if (!operation.TrySerializeValueParameter(DocumentSerializer, out Stream stream))
            {
                return JValue.CreateNull();
            }

            using (stream)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return JToken.Parse(reader.ReadToEnd());
            }
        }

        private static ItemResponse<FhirCosmosResourceWrapper> CreateItemResponse(FhirCosmosResourceWrapper wrapper)
        {
            ItemResponse<FhirCosmosResourceWrapper> response = Substitute.For<ItemResponse<FhirCosmosResourceWrapper>>();
            response.Resource.Returns(wrapper);
            return response;
        }

        private ItemResponse<FhirCosmosResourceWrapper> ReadItem()
        {
            // A read served from a snapshot still returns what that snapshot held, even if the document has
            // since been removed - that is exactly what makes a stale read stale.
            JObject source = _snapshotForNextRead ?? _storedDocument;
            _snapshotForNextRead = null;

            if (source == null)
            {
                throw CreateCosmosException(HttpStatusCode.NotFound);
            }

            return CreateItemResponse(Deserialize(source));
        }

        private ItemResponse<FhirCosmosResourceWrapper> ReplaceItem(FhirCosmosResourceWrapper wrapper, ItemRequestOptions requestOptions)
        {
            ThrowIfNotWritable(requestOptions?.IfMatchEtag);

            // Cosmos DB replaces the stored document with whatever the client serialized, so anything the
            // client could not read back in the first place is lost here just as it would be in production.
            JObject document = Serialize(wrapper);
            document[KnownDocumentProperties.ETag] = NextETag();
            _storedDocument = document;
            WriteCount++;

            return CreateItemResponse(wrapper);
        }

        private ItemResponse<FhirCosmosResourceWrapper> PatchItem(IReadOnlyList<PatchOperation> operations, PatchItemRequestOptions requestOptions)
        {
            ThrowIfNotWritable(requestOptions?.IfMatchEtag);

            // Patch operations are applied server side, against the stored document, so no field of the stored
            // document other than the patched paths is touched.
            foreach (PatchOperation operation in operations)
            {
                string path = operation.Path.TrimStart('/');

                if (path.Contains('/', StringComparison.Ordinal))
                {
                    throw new NotSupportedException($"This simulation only applies patch operations to top level document properties, but '{operation.Path}' is nested.");
                }

                switch (operation.OperationType)
                {
                    case PatchOperationType.Set:
                    case PatchOperationType.Add:
                    case PatchOperationType.Replace:
                        _storedDocument[path] = ValueOf(operation);
                        break;
                    default:
                        throw new NotSupportedException($"This simulation does not apply '{operation.OperationType}' patch operations.");
                }
            }

            _storedDocument[KnownDocumentProperties.ETag] = NextETag();
            WriteCount++;

            return CreateItemResponse(null);
        }

        private void ThrowIfNotWritable(string ifMatchETag)
        {
            WriteAttemptCount++;

            if (WriteAttemptCount > WriteAttemptSafetyLimit)
            {
                throw new InvalidOperationException(
                    $"The code under test has issued {WriteAttemptCount} write requests against this container. A bounded retry policy would have given up long before that, so this is reported as an unbounded retry loop instead of being left to spin.");
            }

            if (AConcurrentWriterWinsEveryRace && _storedDocument != null)
            {
                _storedDocument[KnownDocumentProperties.ETag] = NextETag();
            }

            if (_storedDocument == null)
            {
                throw CreateCosmosException(HttpStatusCode.NotFound);
            }

            if (ifMatchETag != null && !string.Equals(ifMatchETag, StoredETag, StringComparison.Ordinal))
            {
                throw CreateCosmosException(HttpStatusCode.PreconditionFailed);
            }
        }

        private string NextETag() => "\"etag-" + (++_etagSequence).ToString(CultureInfo.InvariantCulture) + "\"";
    }
}
