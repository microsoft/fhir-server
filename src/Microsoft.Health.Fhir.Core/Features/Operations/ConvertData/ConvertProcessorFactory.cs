// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Liquid.Converter.Models;
using Microsoft.Health.Fhir.Liquid.Converter.Processors;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData
{
    public class ConvertProcessorFactory : IConvertProcessorFactory
    {
        private readonly ConvertDataConfiguration _convertDataConfiguration;
        private readonly ILoggerFactory _loggerFactory;

        public ConvertProcessorFactory(
            IOptions<ConvertDataConfiguration> convertDataConfiguration,
            ILoggerFactory loggerFactory)
        {
            _convertDataConfiguration = EnsureArg.IsNotNull(convertDataConfiguration, nameof(convertDataConfiguration)).Value;
            _loggerFactory = EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));
        }

        public IFhirConverter GetProcessor(DataType inputDataType)
        {
            var processorSetting = new ProcessorSettings
            {
                TimeOut = (int)_convertDataConfiguration.OperationTimeout.TotalMilliseconds,
                EnableTelemetryLogger = _convertDataConfiguration.EnableTelemetryLogger,
            };

            IFhirConverter converter = null;

            switch (inputDataType)
            {
                case DataType.Ccda:
                    converter = new CcdaProcessor(processorSetting, _loggerFactory.CreateLogger<CcdaProcessor>());
                    break;
                case DataType.Fhir:
                    converter = new FhirProcessor(processorSetting, _loggerFactory.CreateLogger<FhirProcessor>());
                    break;
                case DataType.Hl7v2:
                    converter = new Hl7v2Processor(processorSetting, _loggerFactory.CreateLogger<Hl7v2Processor>());
                    break;
                case DataType.Json:
                    converter = new JsonProcessor(processorSetting, _loggerFactory.CreateLogger<JsonProcessor>());
                    break;
                default:
                    throw new InvalidOperationException($"Input Data Type {inputDataType.ToString()} is not supported.");
            }

            return converter;
        }
    }
}
