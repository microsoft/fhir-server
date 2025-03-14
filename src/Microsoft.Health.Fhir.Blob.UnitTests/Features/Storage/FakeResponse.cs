// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Azure;
using Azure.Core;

namespace Microsoft.Health.Fhir.Blob.UnitTests.Features.Storage
{
    internal class FakeResponse : Response
    {
        private readonly int _status;
        private readonly string _reasonPhrase;

        public FakeResponse(int status = 200, string reasonPhrase = "OK")
        {
            _status = status;
            _reasonPhrase = reasonPhrase;
        }

        public override int Status => _status;

        public override string ReasonPhrase => _reasonPhrase;

        // Settable so the content can be manipulated in tests if needed.
        public override Stream ContentStream { get; set; }

        public override string ClientRequestId { get; set; } = string.Empty;

        public override ResponseHeaders Headers { get; }

        public override void Dispose()
        {
            /* No resources to dispose in this fake. */
        }

        protected override bool TryGetHeader(string name, out string value)
        {
            value = string.Empty;
            return false;
        }

        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
        {
            values = Array.Empty<string>();
            return false;
        }

        protected override IEnumerable<HttpHeader> EnumerateHeaders()
        {
            return Array.Empty<HttpHeader>();
        }

        protected override bool ContainsHeader(string name)
        {
            return false;
        }
    }
}
