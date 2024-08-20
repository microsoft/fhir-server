﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class RawResource
    {
        private Lazy<string> _data;

        public RawResource(string data, FhirResourceFormat format, bool isMetaSet)
        {
            EnsureArg.IsNotNullOrEmpty(data, nameof(data));

            Data = data;
            Format = format;
            IsMetaSet = isMetaSet;
        }

        public RawResource(Lazy<string> data, FhirResourceFormat format, bool isMetaSet)
        {
            EnsureArg.IsNotNull(data, nameof(data));

            _data = data;
            Format = format;
            IsMetaSet = isMetaSet;
        }

        [JsonConstructor]
        protected RawResource()
        {
        }

        [JsonProperty("data")]
        public string Data
        {
            get
            {
                return _data.Value;
            }

            protected set
            {
                _data = new Lazy<string>(() => value);
            }
        }

        [JsonProperty("format")]
        [JsonConverter(typeof(StringEnumConverter))]
        public FhirResourceFormat Format { get; protected set; }

        /// <summary>
        /// Specifies whether the meta section in the serialized resource in Data is set correctly.
        /// We expect that for a RawResource resulting from an update, the version needs to be updated, so isMetaSet would be false.
        /// While on a RawResource resulting from a create, the version should be correct and isMetaSet would be true.
        /// </summary>
        [JsonProperty("isMetaSet")]
        public bool IsMetaSet { get; set; }
    }
}
