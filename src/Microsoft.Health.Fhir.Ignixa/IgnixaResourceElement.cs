// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.Serialization.SourceNodes;

namespace Microsoft.Health.Fhir.Ignixa;

/// <summary>
/// Wraps a <see cref="ResourceJsonNode"/> to provide schema-aware FHIR resource access.
/// </summary>
/// <remarks>
/// <para>
/// This class bridges Ignixa's mutable JSON-based resource model with the existing FHIR server infrastructure.
/// It provides:
/// </para>
/// <list type="bullet">
/// <item><description>Direct access to the underlying <see cref="ResourceJsonNode"/> for mutations and serialization</description></item>
/// <item><description>Schema-aware access via <see cref="IElement"/> for validation and search indexing</description></item>
/// <item><description>Firely SDK compatibility via <see cref="ITypedElement"/> for FhirPath evaluation</description></item>
/// </list>
/// </remarks>
public class IgnixaResourceElement : IResourceElement
{
    private readonly ResourceJsonNode _resourceNode;
    private readonly ISchema _schema;
    private IElement? _cachedElement;
    private ITypedElement? _cachedTypedElement;

    /// <summary>
    /// Initializes a new instance of the <see cref="IgnixaResourceElement"/> class.
    /// </summary>
    /// <param name="resourceNode">The underlying Ignixa resource node.</param>
    /// <param name="schema">The FHIR schema provider for type metadata.</param>
    /// <exception cref="ArgumentNullException">Thrown when resourceNode or schema is null.</exception>
    public IgnixaResourceElement(ResourceJsonNode resourceNode, ISchema schema)
    {
        EnsureArg.IsNotNull(resourceNode, nameof(resourceNode));
        EnsureArg.IsNotNull(schema, nameof(schema));

        _resourceNode = resourceNode;
        _schema = schema;
    }

    /// <summary>
    /// Gets the underlying <see cref="ResourceJsonNode"/> for direct JSON manipulation.
    /// </summary>
    /// <remarks>
    /// Use this for:
    /// <list type="bullet">
    /// <item><description>Applying FHIR Patch operations</description></item>
    /// <item><description>Serializing the resource to JSON</description></item>
    /// <item><description>Updating meta properties (versionId, lastUpdated)</description></item>
    /// </list>
    /// After mutations, call <see cref="InvalidateCaches"/> to ensure subsequent access returns fresh views.
    /// </remarks>
    public ResourceJsonNode ResourceNode => _resourceNode;

    /// <summary>
    /// Gets the Ignixa <see cref="IElement"/> for schema-aware element navigation.
    /// </summary>
    /// <remarks>
    /// The element is cached for performance. Call <see cref="InvalidateCaches"/> after mutations
    /// to ensure the next access creates a fresh element reflecting the current state.
    /// </remarks>
    public IElement Element
    {
        get
        {
            _cachedElement ??= _resourceNode.ToElement(_schema);
            return _cachedElement;
        }
    }

    /// <summary>
    /// Gets the resource type name (e.g., "Patient", "Observation").
    /// </summary>
    public string InstanceType => _resourceNode.ResourceType;

    /// <summary>
    /// Gets the logical resource identifier.
    /// </summary>
    public string Id => _resourceNode.Id;

    /// <summary>
    /// Gets the resource version identifier.
    /// </summary>
    public string VersionId => _resourceNode.Meta.VersionId ?? string.Empty;

    /// <summary>
    /// Gets the timestamp when the resource was last updated.
    /// </summary>
    public DateTimeOffset? LastUpdated => _resourceNode.Meta.LastUpdated;

    /// <summary>
    /// Gets a value indicating whether this is a domain resource (not Bundle, Parameters, or Binary).
    /// </summary>
    public bool IsDomainResource => InstanceType is not ("Bundle" or "Parameters" or "Binary");

    /// <summary>
    /// Converts the Ignixa element to a Firely SDK <see cref="ITypedElement"/> for FhirPath evaluation.
    /// </summary>
    /// <returns>An <see cref="ITypedElement"/> adapter that wraps the Ignixa element.</returns>
    /// <remarks>
    /// <para>
    /// This method uses the <c>Ignixa.Extensions.FirelySdk5</c> shim to provide compatibility
    /// with Firely SDK-based tools including:
    /// </para>
    /// <list type="bullet">
    /// <item><description>FhirPath evaluation via Hl7.FhirPath</description></item>
    /// <item><description>FHIR validation via Firely validators</description></item>
    /// <item><description>Existing code paths that expect ITypedElement</description></item>
    /// </list>
    /// <para>The result is cached for performance.</para>
    /// </remarks>
    public ITypedElement ToTypedElement()
    {
        _cachedTypedElement ??= Element.ToTypedElement();
        return _cachedTypedElement;
    }

    /// <summary>
    /// Evaluates a FhirPath expression and returns a scalar value.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="fhirPath">The FhirPath expression to evaluate.</param>
    /// <returns>The scalar result of the FhirPath evaluation.</returns>
    public T? Scalar<T>(string fhirPath)
    {
        var typedElement = ToTypedElement();
        var result = typedElement.Scalar(fhirPath);
        return result is T typedResult ? typedResult : default;
    }

    /// <summary>
    /// Evaluates a FhirPath expression and returns matching elements.
    /// </summary>
    /// <param name="fhirPath">The FhirPath expression to evaluate.</param>
    /// <returns>An enumerable of matching <see cref="ITypedElement"/> instances.</returns>
    public IEnumerable<ITypedElement> Select(string fhirPath)
    {
        var typedElement = ToTypedElement();
        return typedElement.Select(fhirPath);
    }

    /// <summary>
    /// Evaluates a FhirPath predicate expression.
    /// </summary>
    /// <param name="fhirPath">The FhirPath predicate expression.</param>
    /// <returns>True if the predicate matches; otherwise false.</returns>
    public bool Predicate(string fhirPath)
    {
        var typedElement = ToTypedElement();
        return typedElement.Predicate(fhirPath);
    }

    /// <summary>
    /// Invalidates cached views after in-place mutations to the underlying JSON.
    /// </summary>
    /// <remarks>
    /// Call this method after applying changes via <see cref="ResourceNode"/> to ensure
    /// subsequent access to <see cref="Element"/> or <see cref="ToTypedElement"/> returns
    /// fresh views reflecting the current state.
    /// </remarks>
    public void InvalidateCaches()
    {
        _cachedElement = null;
        _cachedTypedElement = null;
        _resourceNode.InvalidateCaches();
    }

    /// <summary>
    /// Sets the meta.versionId property on the resource.
    /// </summary>
    /// <param name="versionId">The version identifier to set.</param>
    public void SetVersionId(string versionId)
    {
        _resourceNode.Meta.VersionId = versionId;
    }

    /// <summary>
    /// Sets the meta.lastUpdated property on the resource.
    /// </summary>
    /// <param name="lastUpdated">The timestamp to set.</param>
    public void SetLastUpdated(DateTimeOffset lastUpdated)
    {
        _resourceNode.Meta.LastUpdated = lastUpdated;
    }
}
