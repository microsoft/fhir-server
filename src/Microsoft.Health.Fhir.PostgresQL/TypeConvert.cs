// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.PostgresQL
{
    internal class TypeConvert
    {
        internal struct BulkCompartmentAssignmentTableTypeV1Row
        {
            internal BulkCompartmentAssignmentTableTypeV1Row(int offset, byte compartmentTypeId, string referenceResourceId)
            {
                Offset = offset;
                CompartmentTypeId = compartmentTypeId;
                ReferenceResourceId = referenceResourceId;
            }

            internal int Offset { get; }

            internal byte CompartmentTypeId { get; }

            internal string ReferenceResourceId { get; }
        }

        internal struct BulkReferenceSearchParamTableTypeV1Row
        {
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
            internal BulkReferenceSearchParamTableTypeV1Row(int Offset, short SearchParamId, string BaseUri, short? ReferenceResourceTypeId, string ReferenceResourceId, int? ReferenceResourceVersion)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
            {
                this.Offset = Offset;
                this.SearchParamId = SearchParamId;
                this.BaseUri = BaseUri;
                this.ReferenceResourceTypeId = ReferenceResourceTypeId;
                this.ReferenceResourceId = ReferenceResourceId;
                this.ReferenceResourceVersion = ReferenceResourceVersion;
            }

            internal int Offset { get; }

            internal short SearchParamId { get; }

            internal string BaseUri { get; }

            internal short? ReferenceResourceTypeId { get; }

            internal string ReferenceResourceId { get; }

            internal int? ReferenceResourceVersion { get; }
        }

        internal struct BulkTokenSearchParamTableTypeV1Row
        {
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
            internal BulkTokenSearchParamTableTypeV1Row(int Offset, short SearchParamId, int? SystemId, string Code)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
            {
                this.Offset = Offset;
                this.SearchParamId = SearchParamId;
                this.SystemId = SystemId;
                this.Code = Code;
            }

            internal int Offset { get; }

            internal short SearchParamId { get; }

            internal int? SystemId { get; }

            internal string Code { get; }
        }

        internal struct BulkStringSearchParamTableTypeV2Row
        {
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
            internal BulkStringSearchParamTableTypeV2Row(int Offset, short SearchParamId, string Text, string TextOverflow, bool IsMin, bool IsMax)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
            {
                this.Offset = Offset;
                this.SearchParamId = SearchParamId;
                this.Text = Text;
                this.TextOverflow = TextOverflow;
                this.IsMin = IsMin;
                this.IsMax = IsMax;
            }

            internal int Offset { get; }

            internal short SearchParamId { get; }

            internal string Text { get; }

            internal string TextOverflow { get; }

            internal bool IsMin { get; }

            internal bool IsMax { get; }
        }

        internal struct BulkNumberSearchParamTableTypeV1Row
        {
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
            internal BulkNumberSearchParamTableTypeV1Row(int Offset, short SearchParamId, decimal? SingleValue, decimal? LowValue, decimal? HighValue)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
            {
                this.Offset = Offset;
                this.SearchParamId = SearchParamId;
                this.SingleValue = SingleValue;
                this.LowValue = LowValue;
                this.HighValue = HighValue;
            }

            internal int Offset { get; }

            internal short SearchParamId { get; }

            internal decimal? SingleValue { get; }

            internal decimal? LowValue { get; }

            internal decimal? HighValue { get; }
        }

        internal struct BulkQuantitySearchParamTableTypeV1Row
        {
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
            internal BulkQuantitySearchParamTableTypeV1Row(int Offset, short SearchParamId, int? SystemId, int? QuantityCodeId, decimal? SingleValue, decimal? LowValue, decimal? HighValue)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
            {
                this.Offset = Offset;
                this.SearchParamId = SearchParamId;
                this.SystemId = SystemId;
                this.QuantityCodeId = QuantityCodeId;
                this.SingleValue = SingleValue;
                this.LowValue = LowValue;
                this.HighValue = HighValue;
            }

            internal int Offset { get; }

            internal short SearchParamId { get; }

            internal int? SystemId { get; }

            internal int? QuantityCodeId { get; }

            internal decimal? SingleValue { get; }

            internal decimal? LowValue { get; }

            internal decimal? HighValue { get; }
        }

        internal struct BulkUriSearchParamTableTypeV1Row
        {
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
            internal BulkUriSearchParamTableTypeV1Row(int Offset, short SearchParamId, string Uri)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
            {
                this.Offset = Offset;
                this.SearchParamId = SearchParamId;
                this.Uri = Uri;
            }

            internal int Offset { get; }

            internal short SearchParamId { get; }

            internal string Uri { get; }
        }

        internal struct BulkDateTimeSearchParamTableTypeV2Row
        {
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
            internal BulkDateTimeSearchParamTableTypeV2Row(int Offset, short SearchParamId, System.DateTimeOffset StartDateTime, System.DateTimeOffset EndDateTime, bool IsLongerThanADay, bool IsMin, bool IsMax)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
            {
                this.Offset = Offset;
                this.SearchParamId = SearchParamId;
                this.StartDateTime = StartDateTime;
                this.EndDateTime = EndDateTime;
                this.IsLongerThanADay = IsLongerThanADay;
                this.IsMin = IsMin;
                this.IsMax = IsMax;
            }

            internal int Offset { get; }

            internal short SearchParamId { get; }

            internal System.DateTimeOffset StartDateTime { get; }

            internal System.DateTimeOffset EndDateTime { get; }

            internal bool IsLongerThanADay { get; }

            internal bool IsMin { get; }

            internal bool IsMax { get; }
        }

        internal struct BulkReferenceTokenCompositeSearchParamTableTypeV1Row
        {
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
            internal BulkReferenceTokenCompositeSearchParamTableTypeV1Row(int Offset, short SearchParamId, string BaseUri1, short? ReferenceResourceTypeId1, string ReferenceResourceId1, int? ReferenceResourceVersion1, int? SystemId2, string Code2)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
            {
                this.Offset = Offset;
                this.SearchParamId = SearchParamId;
                this.BaseUri1 = BaseUri1;
                this.ReferenceResourceTypeId1 = ReferenceResourceTypeId1;
                this.ReferenceResourceId1 = ReferenceResourceId1;
                this.ReferenceResourceVersion1 = ReferenceResourceVersion1;
                this.SystemId2 = SystemId2;
                this.Code2 = Code2;
            }

            internal int Offset { get; }

            internal short SearchParamId { get; }

            internal string BaseUri1 { get; }

            internal short? ReferenceResourceTypeId1 { get; }

            internal string ReferenceResourceId1 { get; }

            internal int? ReferenceResourceVersion1 { get; }

            internal int? SystemId2 { get; }

            internal string Code2 { get; }
        }

        internal struct BulkTokenTokenCompositeSearchParamTableTypeV1Row
        {
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
            internal BulkTokenTokenCompositeSearchParamTableTypeV1Row(int Offset, short SearchParamId, int? SystemId1, string Code1, int? SystemId2, string Code2)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
            {
                this.Offset = Offset;
                this.SearchParamId = SearchParamId;
                this.SystemId1 = SystemId1;
                this.Code1 = Code1;
                this.SystemId2 = SystemId2;
                this.Code2 = Code2;
            }

            internal int Offset { get; }

            internal short SearchParamId { get; }

            internal int? SystemId1 { get; }

            internal string Code1 { get; }

            internal int? SystemId2 { get; }

            internal string Code2 { get; }
        }

        internal struct BulkTokenDateTimeCompositeSearchParamTableTypeV1Row
        {
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
            internal BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(int Offset, short SearchParamId, int? SystemId1, string Code1, System.DateTimeOffset StartDateTime2, System.DateTimeOffset EndDateTime2, bool IsLongerThanADay2)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
            {
                this.Offset = Offset;
                this.SearchParamId = SearchParamId;
                this.SystemId1 = SystemId1;
                this.Code1 = Code1;
                this.StartDateTime2 = StartDateTime2;
                this.EndDateTime2 = EndDateTime2;
                this.IsLongerThanADay2 = IsLongerThanADay2;
            }

            internal int Offset { get; }

            internal short SearchParamId { get; }

            internal int? SystemId1 { get; }

            internal string Code1 { get; }

            internal System.DateTimeOffset StartDateTime2 { get; }

            internal System.DateTimeOffset EndDateTime2 { get; }

            internal bool IsLongerThanADay2 { get; }
        }

        internal struct BulkTokenQuantityCompositeSearchParamTableTypeV1Row
        {
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
            internal BulkTokenQuantityCompositeSearchParamTableTypeV1Row(int Offset, short SearchParamId, int? SystemId1, string Code1, int? SystemId2, int? QuantityCodeId2, decimal? SingleValue2, decimal? LowValue2, decimal? HighValue2)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
            {
                this.Offset = Offset;
                this.SearchParamId = SearchParamId;
                this.SystemId1 = SystemId1;
                this.Code1 = Code1;
                this.SystemId2 = SystemId2;
                this.QuantityCodeId2 = QuantityCodeId2;
                this.SingleValue2 = SingleValue2;
                this.LowValue2 = LowValue2;
                this.HighValue2 = HighValue2;
            }

            internal int Offset { get; }

            internal short SearchParamId { get; }

            internal int? SystemId1 { get; }

            internal string Code1 { get; }

            internal int? SystemId2 { get; }

            internal int? QuantityCodeId2 { get; }

            internal decimal? SingleValue2 { get; }

            internal decimal? LowValue2 { get; }

            internal decimal? HighValue2 { get; }
        }

        internal struct BulkTokenStringCompositeSearchParamTableTypeV1Row
        {
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
            internal BulkTokenStringCompositeSearchParamTableTypeV1Row(int Offset, short SearchParamId, int? SystemId1, string Code1, string Text2, string TextOverflow2)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
            {
                this.Offset = Offset;
                this.SearchParamId = SearchParamId;
                this.SystemId1 = SystemId1;
                this.Code1 = Code1;
                this.Text2 = Text2;
                this.TextOverflow2 = TextOverflow2;
            }

            internal int Offset { get; }

            internal short SearchParamId { get; }

            internal int? SystemId1 { get; }

            internal string Code1 { get; }

            internal string Text2 { get; }

            internal string TextOverflow2 { get; }
        }

        internal struct BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row
        {
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
            internal BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(int Offset, short SearchParamId, int? SystemId1, string Code1, decimal? SingleValue2, decimal? LowValue2, decimal? HighValue2, decimal? SingleValue3, decimal? LowValue3, decimal? HighValue3, bool HasRange)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
            {
                this.Offset = Offset;
                this.SearchParamId = SearchParamId;
                this.SystemId1 = SystemId1;
                this.Code1 = Code1;
                this.SingleValue2 = SingleValue2;
                this.LowValue2 = LowValue2;
                this.HighValue2 = HighValue2;
                this.SingleValue3 = SingleValue3;
                this.LowValue3 = LowValue3;
                this.HighValue3 = HighValue3;
                this.HasRange = HasRange;
            }

            internal int Offset { get; }

            internal short SearchParamId { get; }

            internal int? SystemId1 { get; }

            internal string Code1 { get; }

            internal decimal? SingleValue2 { get; }

            internal decimal? LowValue2 { get; }

            internal decimal? HighValue2 { get; }

            internal decimal? SingleValue3 { get; }

            internal decimal? LowValue3 { get; }

            internal decimal? HighValue3 { get; }

            internal bool HasRange { get; }
        }

        internal class BulkResourceWriteClaimTableTypeV1Row
        {
            public BulkResourceWriteClaimTableTypeV1Row()
            {
            }

            public int Offset { get; set; }

#pragma warning disable SA1300 // Element should begin with upper-case letter
            public int claimtypeid { get; set; }

            public string? claimvalue { get; set; }
#pragma warning restore SA1300 // Element should begin with upper-case letter

        }

        internal class BulkTokenTextTableTypeV1Row
        {
            public BulkTokenTextTableTypeV1Row()
            {
            }
#pragma warning disable SA1300 // Element should begin with upper-case letter

            public int offsetid { get; set; }

            public int searchparamid { get; set; }

            public string? text { get; set; }
#pragma warning restore SA1300 // Element should begin with upper-case letter
        }

        internal class BulkImportResourceType
        {
            public BulkImportResourceType()
            {
            }

#pragma warning disable SA1300 // Element should begin with upper-case letter
            public short resourcetypeid { get; set; }

            public string? resourceid { get; set; }

            public int version { get; set; }

            public bool ishistory { get; set; }

            public long resourcesurrogateid { get; set; }

            public bool isdeleted { get; set; }

            public string? requestmethod { get; set; }

            public Stream? rawresource { get; set; }

            public bool israwresourcemetaset { get; set; }

            public string? searchparamhash { get; set; }
#pragma warning restore SA1300 // Element should begin with upper-case letter
        }
    }
}
