// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Ignixa.Features.Persistence
{
    /// <summary>
    /// An <see cref="IRawResourceFactory"/> that serializes straight from the JSON document Ignixa parsed,
    /// instead of rebuilding a Firely POCO and re-serializing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the $import path this removes the whole Firely round trip that Phase 0 left in place: the resource is
    /// parsed once by Ignixa and the parsed document is written back out directly. For resources that did not
    /// come from Ignixa's parser - ordinary HTTP ingress, or anything read back from the database - there is no
    /// JSON document to reuse and this defers to the Firely factory. That decision is made once per resource by
    /// inspecting where the element came from; it is not an exception-driven retry, and it never re-runs a
    /// serialization that already succeeded.
    /// </para>
    /// </remarks>
    public sealed class IgnixaRawResourceFactory : IRawResourceFactory
    {
        private const string MetaProperty = "meta";
        private const string MetaVersionId = "versionId";
        private const string MetaLastUpdated = "lastUpdated";

        /// <summary>
        /// Firely writes JSON with a relaxed escaper. <c>System.Text.Json</c>'s default escapes <c>+</c>, <c>&amp;</c>,
        /// <c>&lt;</c>, <c>&gt;</c>, <c>'</c> and every non-ASCII character, which would change the bytes we persist
        /// and hand back to clients verbatim - a <c>lastUpdated</c> offset would be stored as
        /// <c>2024-01-02T03:04:05.678\u002B00:00</c>. Ignixa's own <c>JsonSourceNodeFactory.SerializeToString</c>
        /// uses that default escaper, so this type writes the document itself rather than calling it.
        /// </summary>
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly IRawResourceFactory _firelyRawResourceFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaRawResourceFactory"/> class.
        /// </summary>
        /// <param name="firelyRawResourceFactory">
        /// The Firely-backed factory used for resources that did not originate from Ignixa's JSON parser.
        /// </param>
        public IgnixaRawResourceFactory(IRawResourceFactory firelyRawResourceFactory)
        {
            EnsureArg.IsNotNull(firelyRawResourceFactory, nameof(firelyRawResourceFactory));

            _firelyRawResourceFactory = firelyRawResourceFactory;
        }

        /// <inheritdoc />
        public RawResource Create(ResourceElement resource, bool keepMeta, bool keepVersion = false)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            JsonObject json = IgnixaElementAccessor.TryGetBackingJson(resource.Instance);
            if (json == null)
            {
                return _firelyRawResourceFactory.Create(resource, keepMeta, keepVersion);
            }

            JsonObject meta = GetOrCreateMeta(json);
            NormalizeLastUpdated(meta);

            // Read defensively rather than with GetValue<string>(): versionId is a FHIR string, but this is
            // attacker-supplied NDJSON and a JSON number or boolean here would otherwise throw
            // InvalidOperationException out of the import worker. The import parser normalises versionId before
            // this point, so the guard is belt and braces rather than a known path.
            string originalVersionId = meta[MetaVersionId] is JsonValue value && value.GetValueKind() == JsonValueKind.String
                ? value.GetValue<string>()
                : null;

            try
            {
                // Mirrors RawResourceFactory exactly, including the asymmetry: the version is restored only when
                // meta was not kept, so a "1" written for the keepMeta && !keepVersion case remains on the
                // in-memory resource just as it does in Firely mode.
                if (!keepMeta)
                {
                    meta.Remove(MetaVersionId);
                }
                else if (!keepVersion)
                {
                    meta[MetaVersionId] = JsonValue.Create("1");
                }

                return new RawResource(json.ToJsonString(SerializerOptions), FhirResourceFormat.Json, keepMeta);
            }
            finally
            {
                if (!keepMeta)
                {
                    if (originalVersionId == null)
                    {
                        meta.Remove(MetaVersionId);
                    }
                    else
                    {
                        meta[MetaVersionId] = JsonValue.Create(originalVersionId);
                    }
                }
            }
        }

        private static JsonObject GetOrCreateMeta(JsonObject json)
        {
            if (json[MetaProperty] is JsonObject existing)
            {
                return existing;
            }

            var meta = new JsonObject();
            json[MetaProperty] = meta;
            return meta;
        }

        /// <summary>
        /// Rewrites <c>meta.lastUpdated</c> in the format the Firely serializer produces.
        /// </summary>
        /// <remarks>
        /// Ignixa's typed <c>MetaJsonNode.LastUpdated</c> setter - which the import parser uses to stamp the
        /// resource - writes the full round-trip form with seven fractional digits
        /// (<c>2026-03-04T05:06:07.1230000+00:00</c>). Firely writes at most milliseconds, trims trailing zeros
        /// (<c>.250</c> becomes <c>.25</c>) and omits the fractional part altogether when it is zero. All of these
        /// are valid FHIR <c>instant</c> values denoting the same moment, but the raw JSON is persisted and
        /// returned to clients byte for byte, so normalizing here keeps the two providers producing identical
        /// documents. The value is server-generated on import, so no client-supplied precision is being discarded.
        /// </remarks>
        private static void NormalizeLastUpdated(JsonObject meta)
        {
            if (meta[MetaLastUpdated] is not JsonValue value ||
                value.GetValueKind() != JsonValueKind.String)
            {
                return;
            }

            string raw = value.GetValue<string>();
            if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
            {
                // Not a value we recognise; leave it exactly as the document had it.
                return;
            }

            string format = parsed.Millisecond == 0
                ? "yyyy-MM-dd'T'HH:mm:sszzz"
                : "yyyy-MM-dd'T'HH:mm:ss.FFFzzz";

            string normalized = parsed.ToString(format, CultureInfo.InvariantCulture);
            if (!string.Equals(normalized, raw, StringComparison.Ordinal))
            {
                meta[MetaLastUpdated] = JsonValue.Create(normalized);
            }
        }
    }
}
