// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;
using System.IO;
using System.Text;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.Formatters.Json.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Newtonsoft.Json;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    internal class FhirJsonOutputFormatter : TextOutputFormatter
    {
        private readonly FhirJsonSerializer _fhirJsonSerializer;
        private readonly ILogger<FhirJsonOutputFormatter> _logger;
        private readonly JsonArrayPool<char> _charPool;

        public FhirJsonOutputFormatter(
            FhirJsonSerializer fhirJsonSerializer,
            ILogger<FhirJsonOutputFormatter> logger,
            ArrayPool<char> charPool)
        {
            EnsureArg.IsNotNull(fhirJsonSerializer, nameof(fhirJsonSerializer));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(charPool, nameof(charPool));

            _fhirJsonSerializer = fhirJsonSerializer;
            _logger = logger;
            _charPool = new JsonArrayPool<char>(charPool);

            SupportedEncodings.Add(Encoding.UTF8);
            SupportedEncodings.Add(Encoding.Unicode);
            SupportedMediaTypes.Add(KnownContentTypes.JsonContentType);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.ApplicationJson);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.TextJson);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.ApplicationAnyJsonSyntax);
        }

        protected override bool CanWriteType(Type type)
        {
            EnsureArg.IsNotNull(type, nameof(type));

            return typeof(Resource).IsAssignableFrom(type);
        }

        public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            EnsureArg.IsNotNull(selectedEncoding, nameof(selectedEncoding));

            HttpResponse response = context.HttpContext.Response;
            using (TextWriter textWriter = context.WriterFactory(response.Body, selectedEncoding))
            using (var jsonWriter = new JsonTextWriter(textWriter))
            {
                jsonWriter.ArrayPool = _charPool;

                if (context.HttpContext.GetIsPretty())
                {
                    jsonWriter.Formatting = Formatting.Indented;
                }

                _fhirJsonSerializer.Serialize((Resource)context.Object, jsonWriter, context.HttpContext.GetSummaryType(_logger));
                await jsonWriter.FlushAsync();
            }
        }
    }
}
