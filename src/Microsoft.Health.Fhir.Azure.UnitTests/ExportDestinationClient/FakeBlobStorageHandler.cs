// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Microsoft.Health.Fhir.Azure.UnitTests.ExportDestinationClient
{
    /// <summary>
    /// In-memory fake of the Azure Blob Storage REST operations used by export: create container,
    /// put blob, stage block, and commit block list. Like the real service, it enforces If-Match on
    /// commits and changes the blob ETag on every write, so tests can simulate storage faults.
    /// </summary>
    internal sealed class FakeBlobStorageHandler : HttpMessageHandler
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, byte[]> _uncommittedBlocks = new Dictionary<string, byte[]>();
        private readonly Dictionary<string, BlobState> _blobs = new Dictionary<string, BlobState>();
        private int _etagCounter;

        /// <summary>
        /// Gets or sets the number of upcoming commits that storage applies but whose response is replaced
        /// with 500 OperationTimedOut, as if the response was lost during throttling.
        /// </summary>
        public int CommitResponsesToLose { get; set; }

        /// <summary>
        /// Gets or sets a status code with which storage rejects commits without applying them.
        /// </summary>
        public HttpStatusCode? RejectCommitsWith { get; set; }

        /// <summary>
        /// Gets or sets a blob name that limits <see cref="RejectCommitsWith"/> to that blob. When null, all commits are rejected.
        /// </summary>
        public string RejectCommitsForBlob { get; set; }

        /// <summary>
        /// Gets the If-Match header of every commit request, or null when the request had none.
        /// </summary>
        public List<string> CommitIfMatchHeaders { get; } = new List<string>();

        /// <summary>
        /// Gets the number of stage block requests.
        /// </summary>
        public int StagedBlockCount { get; private set; }

        /// <summary>
        /// Gets the committed content of a blob, or null when the blob doesn't exist.
        /// </summary>
        /// <param name="blobName">The blob name within the container.</param>
        /// <returns>The blob content as UTF-8 text.</returns>
        public string GetBlobContent(string blobName)
        {
            lock (_sync)
            {
                string key = _blobs.Keys.SingleOrDefault(k => k.EndsWith("/" + blobName, StringComparison.Ordinal));
                return key is null ? null : Encoding.UTF8.GetString(_blobs[key].Content);
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Send(request, cancellationToken));
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            byte[] body = request.Content is null ? Array.Empty<byte>() : request.Content.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult();
            Dictionary<string, string> query = ParseQuery(request.RequestUri.Query);
            string path = request.RequestUri.AbsolutePath;

            lock (_sync)
            {
                if (request.Method != HttpMethod.Put)
                {
                    return CreateError(HttpStatusCode.NotImplemented, "NotImplemented");
                }

                if (query.TryGetValue("restype", out string restype) && restype == "container")
                {
                    return CreateSuccess(HttpStatusCode.Created, NextETag());
                }

                query.TryGetValue("comp", out string comp);
                switch (comp)
                {
                    case "block":
                        StagedBlockCount++;
                        _uncommittedBlocks[path + "|" + query["blockid"]] = body;
                        return CreateSuccess(HttpStatusCode.Created, etag: null);

                    case "blocklist":
                        return CommitBlockList(request, path, body);

                    case null:
                        string etag = NextETag();
                        _blobs[path] = new BlobState(body, etag);
                        return CreateSuccess(HttpStatusCode.Created, etag);

                    default:
                        return CreateError(HttpStatusCode.NotImplemented, "NotImplemented");
                }
            }
        }

        private HttpResponseMessage CommitBlockList(HttpRequestMessage request, string path, byte[] body)
        {
            string ifMatch = request.Headers.IfMatch.Count == 0 ? null : request.Headers.IfMatch.First().Tag;
            CommitIfMatchHeaders.Add(ifMatch);

            if (RejectCommitsWith is HttpStatusCode rejectStatus
                && (RejectCommitsForBlob is null || path.EndsWith("/" + RejectCommitsForBlob, StringComparison.Ordinal)))
            {
                return CreateError(rejectStatus, "AuthorizationFailure");
            }

            if (ifMatch is not null && (!_blobs.TryGetValue(path, out BlobState current) || current.ETag != ifMatch))
            {
                return CreateError(HttpStatusCode.PreconditionFailed, "ConditionNotMet");
            }

            IEnumerable<string> blockIds = XDocument.Load(new MemoryStream(body)).Root.Elements().Select(e => e.Value);
            var content = new List<byte>();
            foreach (string blockId in blockIds)
            {
                if (!_uncommittedBlocks.TryGetValue(path + "|" + blockId, out byte[] block))
                {
                    return CreateError(HttpStatusCode.BadRequest, "InvalidBlockList");
                }

                content.AddRange(block);
            }

            string etag = NextETag();
            _blobs[path] = new BlobState(content.ToArray(), etag);

            if (CommitResponsesToLose > 0)
            {
                CommitResponsesToLose--;
                return CreateError(HttpStatusCode.InternalServerError, "OperationTimedOut");
            }

            return CreateSuccess(HttpStatusCode.Created, etag);
        }

        private string NextETag()
        {
            return string.Format(CultureInfo.InvariantCulture, "\"0x{0:X16}\"", ++_etagCounter);
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            return query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .ToDictionary(kv => kv[0], kv => kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty);
        }

        private static HttpResponseMessage CreateSuccess(HttpStatusCode statusCode, string etag)
        {
            var response = new HttpResponseMessage(statusCode) { Content = new ByteArrayContent(Array.Empty<byte>()) };
            if (etag is not null)
            {
                response.Headers.TryAddWithoutValidation("ETag", etag);
            }

            response.Content.Headers.TryAddWithoutValidation("Last-Modified", DateTimeOffset.UtcNow.ToString("R", CultureInfo.InvariantCulture));
            response.Headers.TryAddWithoutValidation("x-ms-request-server-encrypted", "true");
            return response;
        }

        private static HttpResponseMessage CreateError(HttpStatusCode statusCode, string errorCode)
        {
            string xml = $"<?xml version=\"1.0\" encoding=\"utf-8\"?><Error><Code>{errorCode}</Code><Message>Simulated {errorCode}.</Message></Error>";
            var response = new HttpResponseMessage(statusCode) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") };
            response.Headers.TryAddWithoutValidation("x-ms-error-code", errorCode);
            return response;
        }

        private sealed record BlobState(byte[] Content, string ETag);
    }
}
