using System;
using System.Text.Json.Serialization;

namespace ResourceProcessorNamespace.DocumentReference
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public Text text { get; set; }
        public Contained[] contained { get; set; }
        public Masteridentifier masterIdentifier { get; set; }
        public Identifier1[] identifier { get; set; }
        public string status { get; set; }
        public string docStatus { get; set; }
        public Type type { get; set; }
        public Category[] category { get; set; }
        public Subject subject { get; set; }
        public DateTime? date { get; set; }
        public Author[] author { get; set; }
        public Authenticator authenticator { get; set; }
        public Custodian custodian { get; set; }
        public Relatesto[] relatesTo { get; set; }
        public string description { get; set; }
        public Securitylabel[] securityLabel { get; set; }
        public Content[] content { get; set; }
        public Context context { get; set; }
    }

    public class Text
    {
        public string status { get; set; }
        public string div { get; set; }
    }

    public class Masteridentifier
    {
        public string system { get; set; }
        public string value { get; set; }
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

    public class Subject
    {
        public string reference { get; set; }
    }

    public class Authenticator
    {
        public string reference { get; set; }
    }

    public class Custodian
    {
        public string reference { get; set; }
    }

    public class Context
    {
        public Encounter[] encounter { get; set; }
        [JsonPropertyName("event")]
        public Event[] _event { get; set; }
        public Period period { get; set; }
        public Facilitytype facilityType { get; set; }
        public Practicesetting practiceSetting { get; set; }
        public Sourcepatientinfo sourcePatientInfo { get; set; }
        public Related[] related { get; set; }
    }

    public class Period
    {
        public DateTime? start { get; set; }
        public DateTime? end { get; set; }
    }

    public class Facilitytype
    {
        public Coding1[] coding { get; set; }
    }

    public class Coding1
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Practicesetting
    {
        public Coding2[] coding { get; set; }
    }

    public class Coding2
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Sourcepatientinfo
    {
        public string reference { get; set; }
    }

    public class Encounter
    {
        public string reference { get; set; }
    }

    public class Event
    {
        public Coding3[] coding { get; set; }
    }

    public class Coding3
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Related
    {
        public string reference { get; set; }
        public Identifier identifier { get; set; }
    }

    public class Identifier
    {
        public string system { get; set; }
        public string value { get; set; }
    }

    public class Contained
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public Name[] name { get; set; }
    }

    public class Name
    {
        public string family { get; set; }
        public string[] given { get; set; }
    }

    public class Identifier1
    {
        public string system { get; set; }
        public string value { get; set; }
    }

    public class Category
    {
        public Coding4[] coding { get; set; }
    }

    public class Coding4
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Author
    {
        public string reference { get; set; }
    }

    public class Relatesto
    {
        public string code { get; set; }
        public Target target { get; set; }
    }

    public class Target
    {
        public string reference { get; set; }
    }

    public class Securitylabel
    {
        public Coding5[] coding { get; set; }
    }

    public class Coding5
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Content
    {
        public Attachment attachment { get; set; }
        public Format format { get; set; }
    }

    public class Attachment
    {
        public string contentType { get; set; }
        public string language { get; set; }
        public string url { get; set; }
        public int? size { get; set; }
        public string hash { get; set; }
        public string title { get; set; }
        public DateTime? creation { get; set; }
    }

    public class Format
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }
}
