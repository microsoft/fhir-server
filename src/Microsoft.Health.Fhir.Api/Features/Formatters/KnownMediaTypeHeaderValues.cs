// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    internal class KnownMediaTypeHeaderValues
    {
        public static readonly MediaTypeHeaderValue ApplicationJson = MediaTypeHeaderValue.Parse("application/json").CopyAsReadOnly();
        public static readonly MediaTypeHeaderValue ApplicationXml = MediaTypeHeaderValue.Parse("application/xml").CopyAsReadOnly();
        public static readonly MediaTypeHeaderValue TextJson = MediaTypeHeaderValue.Parse("text/json").CopyAsReadOnly();
        public static readonly MediaTypeHeaderValue TextXml = MediaTypeHeaderValue.Parse("text/xml").CopyAsReadOnly();
        public static readonly MediaTypeHeaderValue ApplicationJsonPatch = MediaTypeHeaderValue.Parse("application/json-patch+json").CopyAsReadOnly();
        public static readonly MediaTypeHeaderValue ApplicationAnyJsonSyntax = MediaTypeHeaderValue.Parse("application/*+json").CopyAsReadOnly();
        public static readonly MediaTypeHeaderValue ApplicationAnyXmlSyntax = MediaTypeHeaderValue.Parse("application/*+xml").CopyAsReadOnly();
    }
}
