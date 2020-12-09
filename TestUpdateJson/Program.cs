using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Newtonsoft.Json;

namespace TestUpdateJson
{
    /// <summary>
    /// https://stackoverflow.com/questions/21695185/change-values-in-json-file-writing-files
    /// https://stackoverflow.com/questions/57978535/save-changes-of-iconfigurationroot-sections-to-its-json-file-in-net-core-2-2
    /// </summary>
    public class WritableJsonConfigurationProvider : JsonConfigurationProvider
    {
        public WritableJsonConfigurationProvider(JsonConfigurationSource source) : base(source)
        {
        }

        public override void Set(string key, string value)
        {
            base.Set(key, value);

            //Get Whole json file and change only passed key with passed value. It requires modification if you need to support change multi level json structure
            var fileFullPath = Source.FileProvider.GetFileInfo(Source.Path).PhysicalPath;
            string json = File.ReadAllText(fileFullPath);
            dynamic jsonObj = JsonConvert.DeserializeObject(json);
            jsonObj[key] = value;
            string output = JsonConvert.SerializeObject(jsonObj, Formatting.Indented);
            File.WriteAllText(fileFullPath, output);
        }
    }

    public class WritableJsonConfigurationSource : JsonConfigurationSource
    {
        public override IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            EnsureDefaults(builder);
            return new WritableJsonConfigurationProvider(this);
        }
    }

    public class AppSettings
    {
        public string ExpirationTimeInMinutes { get; set; }

        //public string log4net.Config": "log4net.config",
        //public string  log4net.Config.Watch": "True",
        //public string  log4net.Internal.Debug": "False",
        public string  MessagingHttpServicePort { get; set; }
        public string MessagingNetTcpServicePort { get; set; }
        public string MessagingServiceAddress { get; set; }
        public string MessagingServiceCommunicationType { get; set; }

    }

    public class ConnectionStrings
    {
        public string MsgSvc { get; set; }
    }

    public class RootObject
    {
        public AppSettings AppSettings { get; set; }

        public ConnectionStrings ConnectionStrings { get; set; }
    }

    class Updater
    {
        private const string FileName = "TestSettings.json";
        private const string AppSettings = "appSettings";
        private const string ExpirationTimeInMinutes = "ExpirationTimeInMinutes";

        public static void UpdateConfig1()
        {
            Console.WriteLine("Hello World!");

            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
            IConfigurationRoot configuration = configurationBuilder.Add<WritableJsonConfigurationSource>(
                s =>
                {
                    s.FileProvider = null;
                    s.Path = FileName;
                    s.Optional = false;
                    s.ReloadOnChange = true;
                    s.ResolveFileProvider();
                }).Build();

            Console.WriteLine(configuration.GetSection(AppSettings).Value);

            //configuration.GetSection("appSettings").Value = "changed from code";
            var section = configuration.GetSection(AppSettings);
            var children = section.GetChildren().ToList();
            var expirationTimeInMinutes =
                children
                    .Where(e => e.Key == ExpirationTimeInMinutes)
                    .FirstOrDefault();

            expirationTimeInMinutes.Value = "10";

            // ExpirationTimeInMinutes

            Console.WriteLine(configuration.GetSection(AppSettings).Value);

            Console.ReadKey();
        }

        public static void UpdateConfig2()
        {
            //Read file to string
            string json = File.ReadAllText(FileName);

            //Deserialize from file to object:
            var rootObject = new RootObject();
            JsonConvert.PopulateObject(json, rootObject);

            //Change Value
            rootObject.AppSettings.ExpirationTimeInMinutes = "10";

            // serialize JSON directly to a file again
            using StreamWriter file = File.CreateText(FileName);
            JsonSerializer serializer = new JsonSerializer()
            {
                Formatting = Formatting.Indented,
            };

            serializer.Serialize(file, rootObject);
        }

        public static void UpdateConfig3()
        {
            string json = File.ReadAllText(FileName);
            dynamic jsonObj = JsonConvert.DeserializeObject(json);
            jsonObj[AppSettings][ExpirationTimeInMinutes] = "10";
            string output = JsonConvert.SerializeObject(jsonObj, Formatting.Indented);
            File.WriteAllText(FileName, output);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Updater.UpdateConfig3();
        }
    }
}
