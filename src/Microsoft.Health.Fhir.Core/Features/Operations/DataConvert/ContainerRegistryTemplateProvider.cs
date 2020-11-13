// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotLiquid;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.TemplateManagement;
using Microsoft.Health.Fhir.TemplateManagement.Exceptions;
using Microsoft.Health.Fhir.TemplateManagement.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.DataConvert
{
    public class ContainerRegistryTemplateProvider : IDataConvertTemplateProvider
    {
        private readonly IContainerRegistryTokenProvider _containerRegistryTokenProvider;
        private readonly ITemplateSetProviderFactory _templateSetProviderFactory;
        private readonly ILogger<ContainerRegistryTemplateProvider> _logger;

        public ContainerRegistryTemplateProvider(
            IContainerRegistryTokenProvider containerRegistryTokenProvider,
            ITemplateSetProviderFactory templateSetProviderFactory,
            ILogger<ContainerRegistryTemplateProvider> logger)
        {
            EnsureArg.IsNotNull(containerRegistryTokenProvider, nameof(containerRegistryTokenProvider));
            EnsureArg.IsNotNull(templateSetProviderFactory, nameof(templateSetProviderFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _containerRegistryTokenProvider = containerRegistryTokenProvider;
            _templateSetProviderFactory = templateSetProviderFactory;
            _logger = logger;
        }

        /// <summary>
        /// Fetch template collection from container registry or built-in archive
        /// </summary>
        /// <param name="templateCollectionReference">Should be in "<registryServer>/<imageName>:<imageTag>" or "<registryServer>/<imageName>@<imageDigest>"  format</param>
        /// <param name="cancellationToken">Cancellation token to cancel the fetch operation.</param>
        /// <returns>Template collection.</returns>
        public async Task<List<Dictionary<string, Template>>> GetTemplateCollectionAsync(string templateCollectionReference, CancellationToken cancellationToken)
        {
            // We have embedded a default template set in the templatemanagement package.
            // If the template set is the default reference, we don't need to retrieve token.
            var registryServer = ExtractRegistryServer(templateCollectionReference);
            var accessToken = string.Empty;
            if (!IsDefaultTemplateReference(templateCollectionReference))
            {
                _logger.LogInformation("Using a custom template set for data conversion.");
                accessToken = await _containerRegistryTokenProvider.GetTokenAsync(registryServer, cancellationToken);
            }
            else
            {
                _logger.LogInformation("Using the default template set for data conversion.");
            }

            try
            {
                var provider = _templateSetProviderFactory.CreateTemplateSetProvider(templateCollectionReference, accessToken);
                return await provider.GetTemplateSetAsync(cancellationToken);
            }
            catch (ContainerRegistryAuthenticationException authEx)
            {
                _logger.LogError(authEx, "Failed to access container registry.");
                throw new ContainerRegistryNotAuthorizedException(string.Format(Resources.ContainerRegistryNotAuthorized, registryServer), authEx);
            }
            catch (ImageFetchException fetchException)
            {
                _logger.LogError(fetchException, "Failed to fetch the templates from remote.");
                throw new FetchTemplateCollectionFailedException(string.Format(Resources.FetchTemplateCollectionFailed, fetchException.Message), fetchException);
            }
            catch (ImageValidationException validationException)
            {
                _logger.LogError(validationException, "Failed to validate the downloaded image.");
                throw new FetchTemplateCollectionFailedException(string.Format(Resources.FetchTemplateCollectionFailed, validationException.Message), validationException);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception: failed to get template set.");
                throw new FetchTemplateCollectionFailedException(string.Format(Resources.FetchTemplateCollectionFailed, ex.Message), ex);
            }
        }

        /// <summary>
        /// Extract registry server from image reference (has been validated in the controller).
        /// </summary>
        /// <param name="imageReferece">image reference</param>
        /// <returns>container registry server</returns>
        private string ExtractRegistryServer(string imageReferece)
        {
            return imageReferece.Split('/').First();
        }

        private bool IsDefaultTemplateReference(string templateReference)
        {
            return string.Equals(ImageInfo.DefaultTemplateImageReference, templateReference, StringComparison.OrdinalIgnoreCase);
        }
    }
}
