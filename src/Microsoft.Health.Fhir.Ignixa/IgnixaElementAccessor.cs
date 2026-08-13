// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Hl7.Fhir.ElementModel;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;

namespace Microsoft.Health.Fhir.Ignixa
{
    /// <summary>
    /// Recovers native Ignixa objects from the Firely-shaped <see cref="ITypedElement"/> carried on a
    /// <see cref="Microsoft.Health.Fhir.Core.Models.ResourceElement"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The $import parser produces its <see cref="ResourceElement"/> by calling <c>IElement.ToTypedElement()</c>,
    /// which returns a <c>TypedElementAdapter</c> wrapping the native element. <c>ToIgnixaElement()</c> detects
    /// that adapter and unwraps it, returning the <em>same</em> native <see cref="IElement"/> instance rather
    /// than allocating a new one, so imported resources reach the Ignixa FHIRPath engine with no conversion at
    /// all. Elements originating anywhere else (a Firely POCO from ordinary HTTP ingress, or a resource read
    /// back from the database) are wrapped in a lazy adapter instead, which still evaluates natively.
    /// </para>
    /// <para>
    /// This is why neither <see cref="Microsoft.Health.Fhir.Core.Models.ResourceElement"/> nor the import parser
    /// needs a side-channel to carry the native node. Note that the two-argument <c>ResourceElement</c>
    /// constructor is <em>not</em> a viable carrier: its <c>ResourceInstance</c> slot is a Firely POCO cache
    /// that <c>ModelExtensions.ToPoco&lt;T&gt;()</c> reads with a hard cast, so storing an Ignixa node there
    /// throws <see cref="System.InvalidCastException"/>.
    /// </para>
    /// </remarks>
    internal static class IgnixaElementAccessor
    {
        /// <summary>
        /// Returns the native Ignixa element for <paramref name="element"/>, unwrapping without allocation when
        /// the element is already Ignixa-backed.
        /// </summary>
        /// <param name="element">The Firely-shaped element.</param>
        /// <returns>The native element.</returns>
        public static IElement ToNative(ITypedElement element)
        {
            return element.ToIgnixaElement();
        }

        /// <summary>
        /// Attempts to recover the live, mutable JSON object backing <paramref name="element"/>.
        /// </summary>
        /// <param name="element">The Firely-shaped element.</param>
        /// <returns>
        /// The backing <see cref="JsonObject"/> when the element originated from Ignixa's JSON parser (the
        /// $import path), or <see langword="null"/> for any other element - notably Firely POCO-backed elements,
        /// which have no JSON representation to reuse. A <see langword="null"/> result is the signal to fall back
        /// to the Firely serializer; it is a structural property of where the element came from, not an error.
        /// </returns>
        public static JsonObject TryGetBackingJson(ITypedElement element)
        {
            // The navigator is the JSON source node the schema-aware element was built over; its own metadata
            // channel exposes the live System.Text.Json object, so mutations made through the typed
            // MetaJsonNode model are already visible here.
            return ToNative(element).Meta<ISourceNavigator>()?.Meta<JsonNode>() as JsonObject;
        }
    }
}
