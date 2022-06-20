namespace ResourceProcessorNamespace.Organization
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public Identifier[] identifier { get; set; }
        public bool? active { get; set; }
        public Type[] type { get; set; }
        public string name { get; set; }
        public Telecom[] telecom { get; set; }
        public Address[] address { get; set; }
    }

    public class Identifier
    {
        public string system { get; set; }
        public string value { get; set; }
    }

    public class Type
    {
        public Coding[] coding { get; set; }
        public string text { get; set; }
    }

    public class Coding
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Telecom
    {
        public string system { get; set; }
        public string value { get; set; }
    }

    public class Address
    {
        public string[] line { get; set; }
        public string city { get; set; }
        public string state { get; set; }
        public string postalCode { get; set; }
        public string country { get; set; }
    }
}
