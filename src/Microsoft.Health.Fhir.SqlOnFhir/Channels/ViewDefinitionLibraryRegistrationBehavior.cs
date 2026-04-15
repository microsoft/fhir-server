// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Hl7.Fhir.ElementModel;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;

namespace Microsoft.Health.Fhir.SqlOnFhir.Channels;

/// <summary>
/// MediatR pipeline behavior that intercepts creation of Library resources containing ViewDefinitions.
/// When a Library resource tagged with the ViewDefinition profile is created, this behavior triggers
/// materialization registration (SQL table creation, population job, subscription setup) via
/// <see cref="IViewDefinitionSubscriptionManager"/>.
/// </summary>
public sealed class ViewDefinitionLibraryRegistrationBehavior :
    IPipelineBehavior<CreateResourceRequest, UpsertResourceResponse>,
    IPipelineBehavior<UpsertResourceRequest, UpsertResourceResponse>
{
    private readonly IViewDefinitionSubscriptionManager _subscriptionManager;
    private readonly ILogger<ViewDefinitionLibraryRegistrationBehavior> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionLibraryRegistrationBehavior"/> class.
    /// </summary>
    public ViewDefinitionLibraryRegistrationBehavior(
        IViewDefinitionSubscriptionManager subscriptionManager,
        ILogger<ViewDefinitionLibraryRegistrationBehavior> logger)
    {
        _subscriptionManager = subscriptionManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UpsertResourceResponse> Handle(
        CreateResourceRequest request,
        RequestHandlerDelegate<UpsertResourceResponse> next,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "ViewDefinitionLibraryRegistrationBehavior invoked for {ResourceType}",
            request.Resource.InstanceType);

        // Let the Library resource be created first
        UpsertResourceResponse response = await next(cancellationToken);

        // Check if this is a Library resource with the ViewDefinition profile
        if (!IsViewDefinitionLibrary(request))
        {
            _logger.LogDebug("Resource is not a ViewDefinition Library, skipping registration");
            return response;
        }

        string? viewDefinitionJson = ExtractViewDefinitionJson(request.Resource.Instance);
        if (string.IsNullOrEmpty(viewDefinitionJson))
        {
            _logger.LogWarning("ViewDefinition Library detected but could not extract ViewDefinition JSON from content");
            return response;
        }

        string libraryId = response.Outcome.RawResourceElement.Id;
        MaterializationTarget? target = ExtractMaterializationTarget(request.Resource.Instance);

        _logger.LogInformation(
            "Library resource '{LibraryId}' contains a ViewDefinition. Triggering materialization registration (target: {Target})",
            libraryId,
            target?.ToString() ?? "default");

        try
        {
            await _subscriptionManager.RegisterAsync(viewDefinitionJson, libraryId, cancellationToken, target);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register ViewDefinition from Library '{LibraryId}'", libraryId);
        }

        return response;
    }

    /// <inheritdoc />
    public async Task<UpsertResourceResponse> Handle(
        UpsertResourceRequest request,
        RequestHandlerDelegate<UpsertResourceResponse> next,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("ViewDefinitionLibraryRegistrationBehavior (Upsert) invoked for {ResourceType}", request.Resource.InstanceType);

        UpsertResourceResponse response = await next(cancellationToken);

        if (!IsViewDefinitionLibraryElement(request.Resource))
        {
            return response;
        }

        string? viewDefinitionJson = ExtractViewDefinitionJson(request.Resource.Instance);
        if (string.IsNullOrEmpty(viewDefinitionJson))
        {
            return response;
        }

        string libraryId = response.Outcome.RawResourceElement.Id;
        MaterializationTarget? target = ExtractMaterializationTarget(request.Resource.Instance);

        _logger.LogInformation(
            "Library resource '{LibraryId}' upserted with ViewDefinition. Triggering materialization registration (target: {Target})",
            libraryId,
            target?.ToString() ?? "default");

        try
        {
            await _subscriptionManager.RegisterAsync(viewDefinitionJson, libraryId, cancellationToken, target);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register ViewDefinition from Library '{LibraryId}'", libraryId);
        }

        return response;
    }

    private bool IsViewDefinitionLibrary(CreateResourceRequest request)
    {
        return IsViewDefinitionLibraryElement(request.Resource);
    }

    private bool IsViewDefinitionLibraryElement(Microsoft.Health.Fhir.Core.Models.ResourceElement resource)
    {
        if (!string.Equals(resource.InstanceType, "Library", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check for ViewDefinition profile in meta.profile
        ITypedElement? meta = resource.Instance.Children("meta").FirstOrDefault();
        if (meta == null)
        {
            _logger.LogDebug("Library resource has no meta element");
            return false;
        }

        var profiles = meta.Children("profile").ToList();
        _logger.LogDebug("Library meta.profile values: [{Profiles}]", string.Join(", ", profiles.Select(p => p.Value?.ToString() ?? "(null)")));

        return profiles.Any(p => string.Equals(
                p.Value?.ToString(),
                ViewDefinitionSubscriptionManager.ViewDefinitionLibraryProfile,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractViewDefinitionJson(ITypedElement resource)
    {
        ITypedElement? content = resource.Children("content").FirstOrDefault();
        if (content == null)
        {
            return null;
        }

        string? contentType = content.Children("contentType").FirstOrDefault()?.Value?.ToString();
        if (!string.Equals(contentType, ViewDefinitionSubscriptionManager.ViewDefinitionContentType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? base64 = content.Children("data").FirstOrDefault()?.Value?.ToString();
        if (string.IsNullOrEmpty(base64))
        {
            return null;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    /// <summary>
    /// Extracts the materialization target from the Library resource's extension, if present.
    /// Returns <c>null</c> if not specified, which lets the registration fall back to the
    /// server-wide <see cref="SqlOnFhirMaterializationConfiguration.DefaultTarget"/>.
    /// </summary>
    private static MaterializationTarget? ExtractMaterializationTarget(ITypedElement resource)
    {
        ITypedElement? extensionElement = resource.Children("extension")
            .FirstOrDefault(ext =>
                string.Equals(
                    ext.Children("url").FirstOrDefault()?.Value?.ToString(),
                    ViewDefinitionSubscriptionManager.MaterializationTargetExtensionUrl,
                    StringComparison.OrdinalIgnoreCase));

        if (extensionElement == null)
        {
            return null;
        }

        string? targetValue = extensionElement.Children("value").FirstOrDefault()?.Value?.ToString();
        if (targetValue != null && Enum.TryParse<MaterializationTarget>(targetValue, ignoreCase: true, out var target))
        {
            return target;
        }

        return null;
    }
}
