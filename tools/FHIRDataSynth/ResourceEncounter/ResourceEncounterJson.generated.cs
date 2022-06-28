using System;
using System.Text.Json.Serialization;

namespace ResourceProcessorNamespace.Encounter
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public string status { get; set; }
        [JsonPropertyName("class")]
        public Class1 _class { get; set; }
        public Type[] type { get; set; }
        public Subject subject { get; set; }
        public Participant[] participant { get; set; }
        public Period period { get; set; }
        public Reasoncode[] reasonCode { get; set; }
        public Serviceprovider serviceProvider { get; set; }
    }

    public class Class1
    {
        public string system { get; set; }
        public string code { get; set; }
    }

    public class Subject
    {
        public string reference { get; set; }
        public string display { get; set; }
    }

    public class Period
    {
        public DateTime? start { get; set; }
        public DateTime? end { get; set; }
    }

    public class Serviceprovider
    {
        public string reference { get; set; }
        public string display { get; set; }
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

    public class Participant
    {
        public Type1[] type { get; set; }
        public Period1 period { get; set; }
        public Individual individual { get; set; }
    }

    public class Period1
    {
        public DateTime? start { get; set; }
        public DateTime? end { get; set; }
    }

    public class Individual
    {
        public string reference { get; set; }
        public string display { get; set; }
    }

    public class Type1
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

    public class Reasoncode
    {
        public Coding2[] coding { get; set; }
    }

    public class Coding2
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }
}
