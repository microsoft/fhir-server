// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Data;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public class CodeSystemResolver : ICodeSystemResolver, IStartable
    {
        private readonly IModelInfoProvider _modelInfoProvider;
        private Dictionary<string, string> _dictionary;

        public CodeSystemResolver(IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _modelInfoProvider = modelInfoProvider;
        }

        public string ResolveSystem(string shortPath)
        {
            EnsureArg.IsNotNullOrWhiteSpace(shortPath, nameof(shortPath));

            if (_dictionary == null)
            {
                throw new InvalidOperationException($"{nameof(CodeSystemResolver)} has not been initialized.");
            }

            if (_dictionary.TryGetValue(NormalizePath(shortPath), out var system))
            {
                return system;
            }

            return null;
        }

        public void Start()
        {
            using Stream file = _modelInfoProvider.OpenVersionedFileStream("resourcepath-codesystem-mappings.json");
            using var reader = new StreamReader(file);
            _dictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(reader.ReadToEnd());
        }

        private string NormalizePath(string path)
        {
            return Regex.Replace(path, "\\[\\w+\\]", string.Empty);
        }
    }
}
