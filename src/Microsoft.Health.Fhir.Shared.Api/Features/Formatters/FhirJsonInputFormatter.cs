// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.IO;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    internal class FhirJsonInputFormatter : TextInputFormatter
    {
        private readonly FhirJsonPocoDeserializer _parser;
        private static readonly RecyclableMemoryStreamManager _recyclableMemoryStreamManager = new();

        public FhirJsonInputFormatter(FhirJsonPocoDeserializer parser)
        {
            EnsureArg.IsNotNull(parser, nameof(parser));

            _parser = parser;

            SupportedEncodings.Add(UTF8EncodingWithoutBOM);
            SupportedEncodings.Add(UTF16EncodingLittleEndian);
            SupportedMediaTypes.Add(KnownContentTypes.JsonContentType);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.ApplicationJson);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.TextJson);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.ApplicationAnyJsonSyntax);
        }

        protected override bool CanReadType(Type type)
        {
            EnsureArg.IsNotNull(type, nameof(type));

            return typeof(Resource).IsAssignableFrom(type);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Reference implementation: https://github.com/aspnet/Mvc/blob/dev/src/Microsoft.AspNetCore.Mvc.Formatters.Json/JsonInputFormatter.cs
        /// Parsing from a stream: https://github.com/ewoutkramer/fhir-net-api/blob/master/src/Hl7.Fhir.Support/Utility/SerializationUtil.cs#L134
        /// </remarks>
        public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context, Encoding encoding)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            EnsureArg.IsNotNull(encoding, nameof(encoding));

            var request = context.HttpContext.Request;

            Exception delayedException = null;
            Resource model = null;

            await using RecyclableMemoryStream memoryStream = _recyclableMemoryStreamManager.GetStream();
            await request.Body.CopyToAsync(memoryStream);
            var jsonBytes = new ReadOnlySpan<byte>(memoryStream.GetBuffer(), 0, (int)memoryStream.Length);
            var jsonReader = new Utf8JsonReader(jsonBytes);

            try
            {
                model = _parser.DeserializeResource(ref jsonReader);
            }
            catch (Exception ex)
            {
                delayedException = ex;
            }

            if (model != null)
            {
                return await InputFormatterResult.SuccessAsync(model);
            }

            // Add model state information to return to the client
            context.ModelState.TryAddModelError(string.Empty, string.Format(Api.Resources.ParsingError, delayedException.Message));

            return await InputFormatterResult.FailureAsync();
        }
    }
}
