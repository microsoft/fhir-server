using System;

namespace ResourceProcessorNamespace.SupplyDelivery
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public string status { get; set; }
        public Patient patient { get; set; }
        public Type type { get; set; }
        public Supplieditem suppliedItem { get; set; }
        public DateTime? occurrenceDateTime { get; set; }
    }

    public class Patient
    {
        public string reference { get; set; }
    }

    public class Type
    {
        public Coding[] coding { get; set; }
    }

    public class Coding
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Supplieditem
    {
        public Quantity quantity { get; set; }
        public Itemcodeableconcept itemCodeableConcept { get; set; }
    }

    public class Quantity
    {
        public int? value { get; set; }
    }

    public class Itemcodeableconcept
    {
        public Coding1[] coding { get; set; }
        public string text { get; set; }
    }

    public class Coding1
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }
}
