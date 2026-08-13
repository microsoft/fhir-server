// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Specification;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;

namespace Microsoft.Health.Fhir.Ignixa.FhirPath
{
    /// <summary>
    /// Presents an Ignixa result element to Firely consumers, reporting FHIRPath system types the way the
    /// Firely engine does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FHIRPath defines the results of functions such as <c>exists()</c> and <c>count()</c> as system types.
    /// Firely reports those as <c>System.Boolean</c> and <c>System.Integer</c>; Ignixa reports the FHIR primitive
    /// name (<c>boolean</c>, <c>integer</c>). For values read straight off a resource the two agree, so the
    /// difference is confined to computed values.
    /// </para>
    /// <para>
    /// This does not, today, change which search value converter the indexer selects: the converters register
    /// both spellings (<c>BooleanToTokenSearchValueConverter</c> declares <c>"boolean", "System.Boolean"</c>,
    /// and the integer, decimal, string, code, date and quantity converters do the same). The mapping exists so
    /// that provider parity is a property of the seam rather than a coincidence of that registration table: a
    /// converter added in future that declares only one spelling would otherwise silently index different values
    /// depending on the configured provider, and the parity tests can assert <see cref="ITypedElement.InstanceType"/>
    /// equality directly instead of asserting the weaker "both names happen to resolve to the same converter".
    /// </para>
    /// <para>
    /// The map is therefore restricted to the system type names the converters actually recognise. Speculative
    /// entries are deliberately omitted: mapping a primitive to a system name Firely does not itself produce
    /// would create the very divergence this type exists to prevent.
    /// </para>
    /// <para>
    /// Computed values are identified by carrying no schema type: <see cref="IElement.Type"/> is populated for
    /// elements navigated out of a resource and null for values produced by evaluation.
    /// </para>
    /// </remarks>
    internal sealed class SystemTypedElementAdapter : ITypedElement
    {
        /// <summary>
        /// Maps a FHIR primitive type name to the FHIRPath system type Firely reports for a computed value of
        /// that kind. Every entry is covered by a parity test asserting that Firely reports the same name for an
        /// equivalent computed expression.
        /// </summary>
        private static readonly Dictionary<string, string> SystemTypeNames = new(StringComparer.Ordinal)
        {
            ["boolean"] = "System.Boolean",
            ["integer"] = "System.Integer",
            ["decimal"] = "System.Decimal",
            ["string"] = "System.String",
            ["date"] = "System.Date",
            ["dateTime"] = "System.DateTime",
            ["Quantity"] = "System.Quantity",
        };

        private readonly ITypedElement _inner;

        private SystemTypedElementAdapter(ITypedElement inner, string instanceType)
        {
            _inner = inner;
            InstanceType = instanceType;
        }

        /// <inheritdoc />
        public string Name => _inner.Name;

        /// <inheritdoc />
        public object Value => _inner.Value;

        /// <inheritdoc />
        public string InstanceType { get; }

        /// <inheritdoc />
        public string Location => _inner.Location;

        /// <inheritdoc />
        public IElementDefinitionSummary Definition => _inner.Definition;

        /// <summary>
        /// Converts an Ignixa element to <see cref="ITypedElement"/>, correcting the reported type when the
        /// element is a computed value whose FHIR primitive name maps to a FHIRPath system type.
        /// </summary>
        /// <param name="element">The Ignixa result element.</param>
        /// <returns>The Firely-shaped element.</returns>
        public static ITypedElement Create(IElement element)
        {
            ITypedElement typedElement = element.ToTypedElement();

            if (element.Type == null &&
                element.InstanceType != null &&
                SystemTypeNames.TryGetValue(element.InstanceType, out string systemTypeName))
            {
                return new SystemTypedElementAdapter(typedElement, systemTypeName);
            }

            return typedElement;
        }

        /// <inheritdoc />
        public IEnumerable<ITypedElement> Children(string name = null) => _inner.Children(name);
    }
}
