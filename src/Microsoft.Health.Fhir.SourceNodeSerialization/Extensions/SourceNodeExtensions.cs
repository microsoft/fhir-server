// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Specification;
using Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes.Models;

namespace Microsoft.Health.Fhir.SourceNodeSerialization.Extensions;

public static class SourceNodeExtensions
{
    // depth-first walk of the node tree
    private static IEnumerable<ISourceNode> AllDescendants(this ISourceNode node)
    {
        EnsureArg.IsNotNull(node, nameof(node));

        foreach (ISourceNode child in node.Children())
        {
            yield return child;
            foreach (ISourceNode g in child.Descendants())
            {
                yield return g;
            }
        }
    }

    private static IEnumerable<ITypedElement> AllDescendants(this ITypedElement node)
    {
        EnsureArg.IsNotNull(node, nameof(node));

        foreach (ITypedElement child in node.Children())
        {
            yield return child;
            foreach (ITypedElement g in child.Descendants())
            {
                yield return g;
            }
        }
    }

    // returns the string value of every "reference" element
    public static IEnumerable<string> GetReferenceValues(this ISourceNode node)
    {
        EnsureArg.IsNotNull(node, nameof(node));

        return node.AllDescendants()
            .Where(n => string.Equals(n.Name, "reference", StringComparison.Ordinal))
            .Select(n => n.Text)
            .Where(v => !string.IsNullOrWhiteSpace(v));
    }

    public static IEnumerable<(string Path, string ReferenceValue)> GetReferenceValues(this ITypedElement root)
    {
        return root
            .AllDescendants()
            .Where(e => e.InstanceType == "Reference")
            .Select(e => (
                path: e.Location, // e.g. "Patient.contact.organization"
                referenceText: e.Children("reference")
                    .FirstOrDefault()?.Value?.ToString()));
    }

    public static bool RemoveExtension(this MetaJsonNode node, string url)
    {
        EnsureArg.IsNotNull(node, nameof(node));
        EnsureArg.IsNotNullOrWhiteSpace(url, nameof(url));

        if (node.Extensions != null)
        {
            ExtensionJsonNode extensionToRemove = node.Extensions.FirstOrDefault(e => string.Equals(e.Url, url, StringComparison.OrdinalIgnoreCase));
            if (extensionToRemove != null)
            {
                return node.Extensions.Remove(extensionToRemove);
            }
        }

        return false;
    }
}
