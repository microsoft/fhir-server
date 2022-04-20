using System;

namespace ResourceProcessorNamespace.Communication
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public Text text { get; set; }
        public Identifier1[] identifier { get; set; }
        public string status { get; set; }
        public Category[] category { get; set; }
        public Subject subject { get; set; }
        public About[] about { get; set; }
        public DateTime? sent { get; set; }
        public Recipient[] recipient { get; set; }
        public Sender sender { get; set; }
        public Payload[] payload { get; set; }
    }

    public class Text
    {
        public string status { get; set; }
        public string div { get; set; }
    }

    public class Subject
    {
        public string reference { get; set; }
    }

    public class Sender
    {
        public Identifier identifier { get; set; }
    }

    public class Identifier
    {
        public string system { get; set; }
        public string value { get; set; }
    }

    public class Identifier1
    {
        public string system { get; set; }
        public string value { get; set; }
    }

    public class Category
    {
        public Coding[] coding { get; set; }
    }

    public class Coding
    {
        public string system { get; set; }
        public string code { get; set; }
    }

    public class About
    {
        public Identifier2 identifier { get; set; }
    }

    public class Identifier2
    {
        public string system { get; set; }
        public string value { get; set; }
    }

    public class Recipient
    {
        public Identifier3 identifier { get; set; }
    }

    public class Identifier3
    {
        public string system { get; set; }
        public string value { get; set; }
    }

    public class Payload
    {
        public Contentattachment contentAttachment { get; set; }
    }

    public class Contentattachment
    {
        public string contentType { get; set; }
        public string data { get; set; }
        public string title { get; set; }
        public DateTime? creation { get; set; }
        public string url { get; set; }
        public int? size { get; set; }
        public string hash { get; set; }
    }
}
