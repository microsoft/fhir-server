using System;
using System.Text.Json.Serialization;

namespace ResourceProcessorNamespace.StructureDefinition
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public Text text { get; set; }
        public string url { get; set; }
        public string name { get; set; }
        public string title { get; set; }
        public string status { get; set; }
        public bool? experimental { get; set; }
        public DateTime? date { get; set; }
        public string kind { get; set; }
        [JsonPropertyName("abstract")]
        public bool? _abstract { get; set; }
        public string type { get; set; }
        public string baseDefinition { get; set; }
        public string derivation { get; set; }
        public Differential differential { get; set; }
    }

    public class Text
    {
        public string status { get; set; }
        public string div { get; set; }
    }

    public class Differential
    {
        public Element[] element { get; set; }
    }

    public class Element
    {
        public string id { get; set; }
        public string path { get; set; }
        public Slicing slicing { get; set; }
        public string sliceName { get; set; }
        public int? min { get; set; }
        public string fixedString { get; set; }
        public Patterncodeableconcept patternCodeableConcept { get; set; }
    }

    public class Slicing
    {
        public Discriminator[] discriminator { get; set; }
        public string description { get; set; }
        public bool? ordered { get; set; }
        public string rules { get; set; }
    }

    public class Discriminator
    {
        public string type { get; set; }
        public string path { get; set; }
    }

    public class Patterncodeableconcept
    {
        public Coding[] coding { get; set; }
    }

    public class Coding
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }
}
