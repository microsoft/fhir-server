using System;

namespace ResourceProcessorNamespace.ImagingStudy
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public Identifier[] identifier { get; set; }
        public string status { get; set; }
        public Subject subject { get; set; }
        public Encounter encounter { get; set; }
        public DateTime? started { get; set; }
        public int? numberOfSeries { get; set; }
        public int? numberOfInstances { get; set; }
        public Series[] series { get; set; }
    }

    public class Subject
    {
        public string reference { get; set; }
    }

    public class Encounter
    {
        public string reference { get; set; }
    }

    public class Identifier
    {
        public string use { get; set; }
        public string system { get; set; }
        public string value { get; set; }
    }

    public class Series
    {
        public string uid { get; set; }
        public int? number { get; set; }
        public Modality modality { get; set; }
        public int? numberOfInstances { get; set; }
        public Bodysite bodySite { get; set; }
        public DateTime? started { get; set; }
        public Instance[] instance { get; set; }
    }

    public class Modality
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Bodysite
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Instance
    {
        public string uid { get; set; }
        public Sopclass sopClass { get; set; }
        public int? number { get; set; }
        public string title { get; set; }
    }

    public class Sopclass
    {
        public string system { get; set; }
        public string code { get; set; }
    }
}
