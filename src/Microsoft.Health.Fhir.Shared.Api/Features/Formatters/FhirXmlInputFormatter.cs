// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    internal class FhirXmlInputFormatter : TextInputFormatter
    {
        private readonly FhirXmlParser _parser;

        public FhirXmlInputFormatter(FhirXmlParser parser)
        {
            EnsureArg.IsNotNull(parser, nameof(parser));

            _parser = parser;
            SupportedEncodings.Add(UTF8EncodingWithoutBOM);
            SupportedEncodings.Add(UTF16EncodingLittleEndian);

            SupportedMediaTypes.Add(KnownContentTypes.XmlContentType);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.ApplicationXml);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.TextXml);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.ApplicationAnyXmlSyntax);
        }

        protected override bool CanReadType(Type type)
        {
            EnsureArg.IsNotNull(type, nameof(type));

            return typeof(Resource).IsAssignableFrom(type);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Reference implementation: https://github.com/aspnet/Mvc/blob/dev/src/Microsoft.AspNetCore.Mvc.Formatters.Xml/XmlDataContractSerializerInputFormatter.cs
        /// </remarks>
        public override Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context, Encoding encoding)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            EnsureArg.IsNotNull(encoding, nameof(encoding));

            context.HttpContext.AllowSynchronousIO();

            var request = context.HttpContext.Request;

            try
            {
                using (var textReader = XmlDictionaryReader.CreateTextReader(request.Body, encoding, XmlDictionaryReaderQuotas.Max, onClose: null))
                {
                    var model = _parser.Parse<Resource>(textReader);
                    return Task.FromResult(InputFormatterResult.Success(model));
                }
            }
            catch (Exception ex)
            {
                context.ModelState.TryAddModelError(string.Empty, ex.Message);
            }

            return Task.FromResult(InputFormatterResult.Failure());
        }
    }
}
