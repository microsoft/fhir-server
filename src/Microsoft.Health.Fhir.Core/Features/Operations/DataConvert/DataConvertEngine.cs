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
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.Core.Messages.DataConvert;
using Microsoft.Health.Fhir.Liquid.Converter;
using Microsoft.Health.Fhir.Liquid.Converter.Exceptions;
using Microsoft.Health.Fhir.Liquid.Converter.Hl7v2;
using Microsoft.Health.Fhir.Liquid.Converter.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.DataConvert
{
    public class DataConvertEngine : IDataConvertEngine
    {
        private readonly IDataConvertTemplateProvider _dataConvertTemplateProvider;
        private readonly DataConvertConfiguration _dataConvertConfiguration;
        private readonly ILogger<DataConvertEngine> _logger;

        private readonly Dictionary<DataConvertInputDataType, IFhirConverter> _dataConverterMap = new Dictionary<DataConvertInputDataType, IFhirConverter>();

        public DataConvertEngine(
            IDataConvertTemplateProvider dataConvertTemplateProvider,
            IOptions<DataConvertConfiguration> dataConvertConfiguration,
            ILogger<DataConvertEngine> logger)
        {
            EnsureArg.IsNotNull(dataConvertTemplateProvider, nameof(dataConvertTemplateProvider));
            EnsureArg.IsNotNull(dataConvertConfiguration, nameof(dataConvertConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _dataConvertTemplateProvider = dataConvertTemplateProvider;
            _dataConvertConfiguration = dataConvertConfiguration.Value;
            _logger = logger;

            InitializeConvertProcessors();
        }

        public async Task<DataConvertResponse> Process(DataConvertRequest convertRequest, CancellationToken cancellationToken)
        {
            var templateCollection = await _dataConvertTemplateProvider.GetTemplateCollectionAsync(convertRequest, cancellationToken);
            var result = GetDataConvertResult(convertRequest, new Hl7v2TemplateProvider(templateCollection));

            return new DataConvertResponse(result);
        }

        private string GetDataConvertResult(DataConvertRequest convertRequest, ITemplateProvider templateProvider)
        {
            var dataConverter = _dataConverterMap.GetValueOrDefault(convertRequest.InputDataType);
            if (dataConverter == null)
            {
                // This case should never happen.
                _logger.LogError("Invalid input data type for conversion.");
                throw new RequestNotValidException("Invalid input data type for conversion.");
            }

            try
            {
                return dataConverter.Convert(convertRequest.InputData, convertRequest.EntryPointTemplate, templateProvider);
            }
            catch (DataParseException dpe)
            {
                _logger.LogError(dpe, "Unable to parse the input data.");
                throw new InputDataParseErrorException(string.Format(Resources.InputDataParseError, convertRequest.InputDataType.ToString()), dpe);
            }
            catch (ConverterInitializeException ie)
            {
                _logger.LogError(ie, "Fail to initialize the convert engine.");
                throw new ConvertEngineInitializeException(Resources.DataConvertEngineInitializeFailed, ie);
            }
            catch (FhirConverterException fce)
            {
                if (fce.InnerException is TimeoutException)
                {
                    _logger.LogError(fce, "Data convert operation timed out.");
                    throw new DataConvertTimeoutException(Resources.DataConvertOperationTimeout, fce.InnerException);
                }

                _logger.LogError(fce, "Data convert process failed.");
                throw new DataConvertFailedException(string.Format(Resources.DataConvertFailed, fce.Message), fce);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception: data convert process failed.");
                throw new DataConvertFailedException(string.Format(Resources.DataConvertFailed, ex.Message), ex);
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
                TimeOut = (int)_dataConvertConfiguration.ProcessTimeoutThreshold.TotalMilliseconds,
            };

            _dataConverterMap.Add(DataConvertInputDataType.Hl7v2, new Hl7v2Processor(processorSetting));
        }
    }
}
