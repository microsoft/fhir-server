using System;

namespace ResourceProcessorNamespace.Claim
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public string status { get; set; }
        public Type type { get; set; }
        public string use { get; set; }
        public Patient patient { get; set; }
        public Billableperiod billablePeriod { get; set; }
        public DateTime? created { get; set; }
        public Provider provider { get; set; }
        public Priority priority { get; set; }
        public Prescription prescription { get; set; }
        public Insurance[] insurance { get; set; }
        public Item[] item { get; set; }
        public Total total { get; set; }
    }

    public class Type
    {
        public Coding[] coding { get; set; }
    }

    public class Coding
    {
        public string system { get; set; }
        public string code { get; set; }
    }

    public class Patient
    {
        public string reference { get; set; }
    }

    public class Billableperiod
    {
        public DateTime? start { get; set; }
        public DateTime? end { get; set; }
    }

    public class Provider
    {
        public string reference { get; set; }
        public string display { get; set; }
    }

    public class Priority
    {
        public Coding1[] coding { get; set; }
    }

    public class Coding1
    {
        public string system { get; set; }
        public string code { get; set; }
    }

    public class Prescription
    {
        public string reference { get; set; }
    }

    public class Total
    {
        public float? value { get; set; }
        public string currency { get; set; }
    }

    public class Insurance
    {
        public int? sequence { get; set; }
        public bool? focal { get; set; }
        public Coverage coverage { get; set; }
    }

    public class Coverage
    {
        public string display { get; set; }
    }

    public class Item
    {
        public int? sequence { get; set; }
        public Productorservice productOrService { get; set; }
        public Encounter[] encounter { get; set; }
    }

    public class Productorservice
    {
        public Coding2[] coding { get; set; }
        public string text { get; set; }
    }

    public class Coding2
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Encounter
    {
        public string reference { get; set; }
    }
}
