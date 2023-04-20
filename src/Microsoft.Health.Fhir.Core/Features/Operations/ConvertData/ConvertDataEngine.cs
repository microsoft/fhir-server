// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models;
using Microsoft.Health.Fhir.Core.Messages.ConvertData;
using Microsoft.Health.Fhir.Liquid.Converter;
using Microsoft.Health.Fhir.Liquid.Converter.Exceptions;
using Microsoft.Health.Fhir.Liquid.Converter.Models;
using Microsoft.Health.Fhir.Liquid.Converter.Processors;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData
{
    public class ConvertDataEngine : IConvertDataEngine
    {
        private readonly TemplateProviderFactory _templateProviderFactory;
        private readonly ConvertDataConfiguration _convertDataConfiguration;
        private readonly ILogger<ConvertDataEngine> _logger;

        private readonly Dictionary<DataType, IFhirConverter> _converterMap = new Dictionary<DataType, IFhirConverter>();

        public ConvertDataEngine(
            TemplateProviderFactory templateProviderFactory,
            IOptions<ConvertDataConfiguration> convertDataConfiguration,
            ILogger<ConvertDataEngine> logger)
        {
            EnsureArg.IsNotNull(templateProviderFactory, nameof(templateProviderFactory));
            EnsureArg.IsNotNull(convertDataConfiguration, nameof(convertDataConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _templateProviderFactory = templateProviderFactory;
            _convertDataConfiguration = convertDataConfiguration.Value;
            _logger = logger;

            InitializeConvertProcessors();
        }

        public async Task<ConvertDataResponse> Process(ConvertDataRequest convertRequest, CancellationToken cancellationToken)
        {
            IConvertDataTemplateProvider convertDataTemplateProvider = _templateProviderFactory.GetTemplateProvider(convertRequest);

            List<Dictionary<string, DotLiquid.Template>> templateCollection;

            try
            {
                templateCollection = await convertDataTemplateProvider.GetTemplateCollectionAsync(convertRequest, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new UnauthorizedAccessException(ex.Message); // TODO: update caught exception and thrown exception
            }

            ITemplateProvider templateProvider = new TemplateProvider(templateCollection);
            if (templateProvider == null)
            {
                // This case should never happen.
                _logger.LogInformation("Invalid input data type for conversion.");
                throw new RequestNotValidException("Invalid input data type for conversion.");
            }

            var result = GetConvertDataResult(convertRequest, templateProvider, cancellationToken);

            return new ConvertDataResponse(result);
        }

        private string GetConvertDataResult(ConvertDataRequest convertRequest, ITemplateProvider templateProvider, CancellationToken cancellationToken)
        {
            var converter = _converterMap.GetValueOrDefault(convertRequest.InputDataType);
            if (converter == null)
            {
                // This case should never happen.
                _logger.LogInformation("Invalid input data type for conversion.");
                throw new RequestNotValidException("Invalid input data type for conversion.");
            }

            try
            {
                return converter.Convert(convertRequest.InputData, convertRequest.RootTemplate, templateProvider, cancellationToken);
            }
            catch (FhirConverterException convertException)
            {
                if (convertException.FhirConverterErrorCode == FhirConverterErrorCode.TimeoutError)
                {
                    _logger.LogError(convertException.InnerException, "Convert data operation timed out.");
                    throw new ConvertDataTimeoutException(Core.Resources.ConvertDataOperationTimeout, convertException.InnerException);
                }

                _logger.LogInformation(convertException, "Convert data failed.");
                throw new ConvertDataFailedException(string.Format(Core.Resources.ConvertDataFailed, convertException.Message), convertException);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception: convert data process failed.");
                throw new ConvertDataUnhandledException(string.Format(Core.Resources.ConvertDataFailed, ex.Message), ex);
            }
        }

        /// <summary>
        /// In order to terminate long running templates, we add timeout setting to DotLiquid rendering context,
        /// which throws a Timeout Exception when render process elapsed longer than timeout threshold.
        /// Reference: https://github.com/dotliquid/dotliquid/blob/master/src/DotLiquid/Context.cs
        /// </summary>
        private void InitializeConvertProcessors()
        {
            var processorSetting = new ProcessorSettings
            {
                TimeOut = (int)_convertDataConfiguration.OperationTimeout.TotalMilliseconds,
            };

            _converterMap.Add(DataType.Hl7v2, new Hl7v2Processor(processorSetting));
            _converterMap.Add(DataType.Ccda, new CcdaProcessor(processorSetting));
            _converterMap.Add(DataType.Json, new JsonProcessor(processorSetting));
            _converterMap.Add(DataType.Fhir, new FhirProcessor(processorSetting));
        }
    }
}
