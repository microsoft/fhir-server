// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.Core.Messages.DataConvert;
using Microsoft.Health.Fhir.Liquid.Converter;
using Microsoft.Health.Fhir.Liquid.Converter.Exceptions;
using Microsoft.Health.Fhir.Liquid.Converter.Hl7v2;
using Microsoft.Health.Fhir.TemplateManagement;
using Microsoft.Health.Fhir.TemplateManager.Exceptions;

namespace Microsoft.Health.Fhir.Core.Features.Operations.DataConvert
{
    public class DataConvertEngine : IDataConvertEngine
    {
        private readonly IContainerRegistryTokenProvider _containerRegistryTokenProvider;
        private readonly ITemplateProviderFactory _templateProviderFactory;
        private readonly ILogger<DataConvertEngine> _logger;

        private readonly Dictionary<DataConvertInputDataType, IFhirConverter> _dataConverterMap = new Dictionary<DataConvertInputDataType, IFhirConverter>();
        private const char ImageRegistryDelimiter = '/';
        private const string DefaultTemplateReference = "microsofthealth/fhirconverter:default";

        public DataConvertEngine(
            IContainerRegistryTokenProvider containerRegistryTokenProvider,
            ITemplateProviderFactory templateProviderFactory,
            ILogger<DataConvertEngine> logger)
        {
            EnsureArg.IsNotNull(containerRegistryTokenProvider, nameof(containerRegistryTokenProvider));
            EnsureArg.IsNotNull(templateProviderFactory, nameof(templateProviderFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _containerRegistryTokenProvider = containerRegistryTokenProvider;
            _templateProviderFactory = templateProviderFactory;
            _logger = logger;

            _dataConverterMap.Add(DataConvertInputDataType.Hl7v2, new Hl7v2Processor());
        }

        public async Task<DataConvertResponse> Process(DataConvertRequest convertRequest, CancellationToken cancellationToken)
        {
            // We have embedded a default template set in the templatemanagement package.
            // If the template set is the default reference, we don't need to retrieve token.
            var accessToken = string.Empty;
            if (!IsDefaultTemplateReference(convertRequest.TemplateSetReference))
            {
                _logger.LogInformation("Using a custom template set for data conversion.");
                var registryServer = ExtractRegistryServer(convertRequest.TemplateSetReference);
                accessToken = await _containerRegistryTokenProvider.GetTokenAsync(registryServer, cancellationToken);
            }
            else
            {
                _logger.LogInformation("Using the default template set for data conversion.");
            }

            // Fetch templates
            ITemplateProvider templateProvider;
            try
            {
                templateProvider = await _templateProviderFactory.CreateAsync(convertRequest.TemplateSetReference, accessToken, cancellationToken);
            }
            catch (ContainerRegistryAuthException authEx)
            {
                _logger.LogError(authEx, "Failed to access container registry: unauthorized.");
                throw new GetTemplateSetFailedException(string.Format(Resources.GetTemplateSetFailed, authEx.Message), authEx);
            }
            catch (DefaultTemplatesInitializeException initEx)
            {
                _logger.LogError(initEx, "Failed to initialize default templates.");
                throw new GetTemplateSetFailedException(string.Format(Resources.GetTemplateSetFailed, initEx.Message), initEx);
            }
            catch (ImageFetchException fetchException)
            {
                _logger.LogError(fetchException, "Failed to fetch the templates from remote.");
                throw new GetTemplateSetFailedException(string.Format(Resources.GetTemplateSetFailed, fetchException.Message), fetchException);
            }
            catch (ImageValidationException validationException)
            {
                _logger.LogError(validationException, "Failed to validate the downloaded image.");
                throw new GetTemplateSetFailedException(string.Format(Resources.GetTemplateSetFailed, validationException.Message), validationException);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception: failed to get template set.");
                throw new GetTemplateSetFailedException(string.Format(Resources.GetTemplateSetFailed, ex.Message), ex);
            }

            var dataConverter = _dataConverterMap.GetValueOrDefault(convertRequest.InputDataType);
            if (dataConverter == null)
            {
                // This case should never happen.
                _logger.LogError("Invalid input data type for conversion.");
                throw new RequestNotValidException("Invalid input data type for conversion.");
            }

            try
            {
                string bundleResult = dataConverter.Convert(convertRequest.InputData, convertRequest.EntryPointTemplate, templateProvider);
                return new DataConvertResponse(bundleResult);
            }
            catch (DataParseException dpe)
            {
                _logger.LogError(dpe, "Unable to parse the input data.");
                throw new InputDataParseErrorException(string.Format(Resources.InputDataParseError, convertRequest.InputDataType.ToString()), dpe);
            }
            catch (InitializeException ie)
            {
                _logger.LogError(ie, "Fail to initialize the convert engine.");
                throw new ConvertEngineInitializeException(Resources.ConvertEngineInitializeFailed, ie);
            }
            catch (FhirConverterException fce)
            {
                _logger.LogError(fce, "Data convert process failed.");
                throw new DataConvertFailedException(string.Format(Resources.DataConvertFailed, fce.Message), fce);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception: data convert process failed.");
                throw new DataConvertFailedException(string.Format(Resources.DataConvertFailed, ex.Message), ex);
            }
        }

        private string ExtractRegistryServer(string templateSetReference)
        {
            var referenceComponents = templateSetReference.Split(ImageRegistryDelimiter);
            if (referenceComponents.Length <= 1 || string.IsNullOrWhiteSpace(referenceComponents.First()))
            {
                _logger.LogError("Templates set reference is invalid: cannot extract registry server.");
                throw new TemplateReferenceInvalidException("Template reference is invalid.");
            }

            return referenceComponents[0];
        }

        private bool IsDefaultTemplateReference(string templateReference)
        {
            return string.Equals(DefaultTemplateReference, templateReference, StringComparison.OrdinalIgnoreCase);
        }
    }
}
