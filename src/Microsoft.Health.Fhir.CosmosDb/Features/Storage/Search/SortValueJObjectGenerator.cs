// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
////using System.Collections.Generic;
////using System.Globalization;
////using System.Linq;
////using System.Text;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Search
{
    public class SortValueJObjectGenerator : ISearchValueVisitor
    {
        private string _prefix;

#pragma warning disable CA2211 // Non-constant fields should not be visible
#pragma warning disable SA1401 // Fields should be private
        public static ConcurrentDictionary<string, int> UriToInt = new ConcurrentDictionary<string, int>();
#pragma warning restore SA1401 // Fields should be private
#pragma warning restore CA2211 // Non-constant fields should not be visible

        private JObject CurrentEntry { get; set; }

        public JObject Generate(SortValue entry)
        {
            EnsureArg.IsNotNull(entry, nameof(entry));

            CreateEntry();

            if (entry.Low != null)
            {
                _prefix = SearchValueConstants.SortLowValueFieldName;
                entry.Low.AcceptVisitor(this);
            }
            else
            {
                AddProperty(SearchValueConstants.SortLowValueFieldName, null);
            }

            if (entry.High != null)
            {
                _prefix = SearchValueConstants.SortHighValueFieldName;
                entry.High.AcceptVisitor(this);
            }
            else
            {
                AddProperty(SearchValueConstants.SortHighValueFieldName, null);
            }

            return CurrentEntry;
        }

        public void Visit(CompositeSearchValue composite)
        {
        }

        public void Visit(DateTimeSearchValue dateTime)
        {
            switch (_prefix)
            {
                case SearchValueConstants.SortLowValueFieldName:
                    AddProperty(_prefix, dateTime.Start.ToString("yyyyMMddHHmmss"));
                    break;
                case SearchValueConstants.SortHighValueFieldName:
                    if (dateTime.End.Year < 9999)
                    {
                        AddProperty(_prefix, dateTime.End.ToString("yyyyMMddHHmmss"));
                    }

                    break;
                default:
                    throw new NotSupportedException();
            }
        }

        public void Visit(NumberSearchValue number)
        {
            switch (_prefix)
            {
                case SearchValueConstants.SortLowValueFieldName:
                    AddProperty(_prefix, number.Low ?? number.High);
                    break;
                case SearchValueConstants.SortHighValueFieldName:
                    AddProperty(_prefix, number.High ?? number.Low);
                    break;
                default:
                    throw new NotSupportedException();
            }
        }

        public void Visit(QuantitySearchValue quantity)
        {
        }

        public void Visit(ReferenceSearchValue reference)
        {
        }

        public void Visit(StringSearchValue s)
        {
            AddProperty(_prefix, s.String.NormalizeAndRemoveAccents().ToUpperInvariant());
        }

        public void Visit(TokenSearchValue token)
        {
        }

        public void Visit(UriSearchValue uri)
        {
            AddProperty(_prefix, uri.Uri);
            ////if (!UriToInt.TryGetValue(uri.Uri, out var key))
            ////{
            ////    lock (UriToInt)
            ////    {
            ////        key = UriToInt.IsEmpty ? 1 : UriToInt.Values.Max() + 1;
            ////        AddProperty(_prefix, key.ToString());
            ////    }
            ////}
            ////else
            ////{
            ////    AddProperty(_prefix, key.ToString());
            ////}
        }

        private void CreateEntry()
        {
            CurrentEntry = new JObject();
        }

        private void AddProperty(string name, object value)
        {
            CurrentEntry.Add(new JProperty(name, value));
        }
    }
}
