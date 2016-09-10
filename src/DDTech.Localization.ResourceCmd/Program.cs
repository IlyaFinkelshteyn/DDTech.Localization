using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Resources;
using System.Runtime.Serialization;
using System.Text;
using System.Web;
using CommandLine;
using CsvHelper;
using DDTech.Common.Extensions;
using DDTech.Tools.LocalizationHelper;

namespace DDTech.Localization.ResourceCmd
{
    public enum LocalizationHelperMode
    {
        Translate = 0,
        Handback,
        Handoff
    }

    class Options
    {
        [Option('m', "mode", HelpText = "Operation Mode ([Translate], Handback, Handoff)")]
        public LocalizationHelperMode Mode { get; set; }

        [Option('d', "dir", Required = true, HelpText = "Enlistment Directory")]
        public string EnlistmentDir { get; set; }

        [Option('i', "input", HelpText = "Input File")]
        public string InputFile { get; set; }

        [Option('r', "read", HelpText = "Input files to be processed")]
        public IEnumerable<string> InputFiles { get; set; }
        
        [Option('v', "verbose", HelpText = "Prints all messages to standard output")]
        public bool Verbose { get; set; }

        [Option('l', "locales", HelpText = "Limit to specific locales")]
        public IEnumerable<string> Locales { get; set; }
    }

    class Program
    {
        public static string s_authToken;
        public static DateTime s_authExpireDate;
        public const string BING_TRANSLATE_ENDPOINT_FORMAT = "http://api.microsofttranslator.com/v2/Http.svc/Translate?text={0}&from={1}&to={2}";
        public const string TRANSLATIONS_PATH_FORMAT = "{0}\\src\\DDTech.Localization";
        public const string BASELINE_RESX_FORMAT = "{0}.resx";
        public const string LOCALIZED_RESX_FORMAT = "{0}.{1}.resx";

        private static string s_baselineCulture = CultureHelper.GetDefaultCulture();
        private static Config s_config = "config.json".CreateFromJsonFile<Config>();

        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var result = CommandLine.Parser.Default.ParseArguments<Options>(args);
            var exitCode = result.MapResult(
                Run, 
                errors =>
                {
                    foreach (var error in errors)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(error);
                    }
                    Console.ResetColor();
                    return 1;
                });

            return exitCode;
        }

        static int Run(Options options)
        {
            var locales = CultureHelper.SupportedCultures;
            locales.Remove(s_baselineCulture);

            if (options.Verbose)
            {
                Console.WriteLine("Filenames: {0}", string.Join(",", options.InputFiles.ToArray()));
            }
            
            // Whitelisted locales?
            if (options.Locales.Any())
            {
                if (options.Verbose)
                {
                    Console.WriteLine("Using specific locales: {0}", string.Join(",", options.Locales.ToArray()));
                }

                locales = options.Locales.ToList();
            }

            // Path to .resx files
            var translationsPath = string.Format(TRANSLATIONS_PATH_FORMAT, options.EnlistmentDir);
            if (!Directory.Exists(translationsPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(string.Format("Couldn't find translation dir '{0}'.", translationsPath));
                Console.ResetColor();
                Environment.Exit(-1);
            }

            // Get all the valid resource names (File name minus .resx)
            DirectoryInfo di = new DirectoryInfo(translationsPath);
            var validResourceFileNames = di.GetFiles("*.resx")
                .Where(obj => obj.Name.IndexOf(".", StringComparison.Ordinal) == ((obj.Name.Length - 4) - 1))
                .Select(obj => obj.Name)
                .Select(obj => obj.Replace(".resx", string.Empty))
                .ToList();

            switch (options.Mode)
            {
                case LocalizationHelperMode.Translate:
                    ProcessUntranslatedStrings(options, translationsPath, validResourceFileNames, locales);
                    break;
                case LocalizationHelperMode.Handback:
                    ProcessLocalizationHandback(options, translationsPath, validResourceFileNames, locales);
                    break;
                case LocalizationHelperMode.Handoff:
                    GenerateLocalizationHandoff(translationsPath, validResourceFileNames, locales);
                    break;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n\nDone!\n\nOutput Directory : '{0}'", Environment.CurrentDirectory);
            Console.WriteLine();
            Console.ResetColor();
            return 0;
        }

        private static void GenerateLocalizationHandoff(string translationsPath, IEnumerable<string> validResourceFileNames, IEnumerable<string> locales)
        {
            foreach (var locale in locales)
            {
                GenerateLocaleHandoff(locale, translationsPath, validResourceFileNames);
            }
        }

        private static void GenerateLocaleHandoff(string locale, string translationsPath,
            IEnumerable<string> validResourceFileNames)
        {
            string outputFilePath = $"RPlusStringResources-{locale}-UTF8.csv";
            using (var fileStream = File.Open(outputFilePath, FileMode.Create, FileAccess.Write))
            {
                Console.WriteLine($"Generating Localization Handoff: {locale}");
                using (var textWriter = new StreamWriter(fileStream, Encoding.UTF8))
                {
                    var csvWriter = new CsvWriter(textWriter);
                    csvWriter.WriteHeader<LocalizedString>();

                    // Go through resource names
                    foreach (var resourceName in validResourceFileNames)
                    {
                        Console.WriteLine($"Writing resource values from {resourceName}");
                        var baselineResources = GetCultureResources(translationsPath, resourceName, s_baselineCulture);
                        var localeResources = GetCultureResources(translationsPath, resourceName, locale);

                        foreach (var sKey in baselineResources.Keys)
                        {
                            csvWriter.WriteRecord(new LocalizedString()
                            {
                                FileName = resourceName,
                                FieldName = sKey,
                                EnglishString = baselineResources.GetItemOrDefault(sKey),
                                TranslatedString = localeResources.GetItemOrDefault(sKey)
                            });
                        }
                    }
                }
            }
        }

        private static void ProcessLocalizationHandback(Options options, string translationsPath, IEnumerable<string> validResourceFileNames, IEnumerable<string> locales)
        {
            var inputFilePath = options.InputFile;
            if (string.IsNullOrEmpty(inputFilePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("No input file specified. Need input file to run in handback mode.");
                Console.ResetColor();
                Environment.Exit(-1);
            }

            if (!File.Exists(inputFilePath))
            {
                inputFilePath = Path.Combine(Environment.CurrentDirectory, inputFilePath);
                if (!File.Exists(inputFilePath))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Couldn't find the input file! Please use the -i flag.");
                    Console.ResetColor();
                    Environment.Exit(-1);
                }
            }

            var fi = new FileInfo(inputFilePath);
            if (fi.Extension != ".csv")
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Only .csv file inputs supported. First column is english string, second is localized string.");
                Console.ResetColor();
                Environment.Exit(-1);
            }

            if (locales.Any() && locales.Count() != 1)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("More than one locale specified. Please use one locale per localization handback.");
                Console.ResetColor();
                Environment.Exit(-1);
            }
            
            // Set our handback locale
            var locale = locales.SingleOrDefault();
            if (string.IsNullOrEmpty(locale))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Specified locale came up null for some reason. Aborting!");
                Console.ResetColor();
                Environment.Exit(-1);
            }

            var handbackMappings = new Dictionary<string, Dictionary<string, LocalizedString>>();
            using (var textReader = File.OpenText(inputFilePath))
            {
                var csv = new CsvReader(textReader);
                var handbackRecords = csv.GetRecords<LocalizedString>().ToList();
                foreach(var obj in handbackRecords)
                {
                    if (!handbackMappings.ContainsKey(obj.FileName))
                        handbackMappings[obj.FileName] = new Dictionary<string, LocalizedString>();
                    handbackMappings[obj.FileName][obj.FieldName] = obj;
                }
            }

            // Go through resource names
            foreach (var resourceName in validResourceFileNames)
            {
                // If we didn't get any resources for this file back in our handback file, then skip it
                if (!handbackMappings.ContainsKey(resourceName))
                    continue;

                var baselineResources = GetCultureResources(translationsPath, resourceName, s_baselineCulture);
                var localeResources = GetCultureResources(translationsPath, resourceName, locale);


                var handbackResources = handbackMappings[resourceName];

                // Go through valid resource keys
                foreach (var baselineResource in baselineResources)
                {
                    var resourceKey = baselineResource.Key;
                    var baselineResourceValue = baselineResource.Value;

                    // If we did not get a value for the current resource in the handback file, then skip it
                    if (!handbackResources.ContainsKey(resourceKey))
                        continue;

                    var handbackItem = handbackResources[resourceKey];

                    // Does the config specify that we ignore this value?
                    if (s_config.Ignore.Contains(baselineResourceValue))
                    {
                        // Let's see if it already exists, and remove it fromt he processing queue if so
                        if (localeResources.ContainsKey(resourceKey))
                        {
                            localeResources.Remove(resourceKey);
                        }

                        // Move on to next key
                        continue;
                    }
                    
                    string localeResourceValue = null;

                    // Do we have any override translations?
                    if (s_config.Custom.ContainsKey(locale) && s_config.Custom[locale].ContainsKey(baselineResourceValue))
                    {
                        Console.WriteLine($"Settings string '{resourceKey}' ({baselineResourceValue}) to config override value");
                        localeResourceValue = s_config.Custom[locale][baselineResourceValue];
                    }

                    if (!string.IsNullOrEmpty(localeResourceValue) && localeResourceValue != handbackItem.TranslatedString)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"localized string '{resourceKey}' ({baselineResourceValue}) is disallowed by the config. New Value is '{handbackItem.TranslatedString}'.");
                        Console.ResetColor();
                        continue;
                    }

                    handbackResources.Remove(resourceKey);
                    localeResourceValue = handbackItem.TranslatedString;

                    // Add or update the value
                    if (!localeResources.ContainsKey(resourceKey))
                    {
                        localeResources.Add(resourceKey, localeResourceValue);
                    }
                    else
                    {
                        localeResources[resourceKey] = localeResourceValue;
                    }
                }

                // Save changes off to the filesystem
                AddOrUpdateResourceFile(translationsPath, localeResources, resourceName, locale);
            }

            if (handbackMappings.Any())
            {
                var outputFilePath = Path.Combine(Environment.CurrentDirectory, $"FailedTranslations-{DateTime.Now:MM.dd.yyyy}-{locale}-UTF8.csv");
                using (var textWriter = new StreamWriter(outputFilePath, false, Encoding.UTF8))
                {
                    var csv = new CsvWriter(textWriter, GetConfiguration());
                    var items = handbackMappings.Select(obj => obj.Value).SelectMany(obj => obj.Values);

                    // Add headers
                    csv.WriteHeader<LocalizedString>();

                    foreach (var item in items)
                    {
                        csv.WriteRecord(item);
                    }
                }
                

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Not all translations were correctly matched. The remaining ({0}) localization have been written to a file for manual resolution.", handbackMappings.Count);
                Console.ResetColor();
                Console.WriteLine(outputFilePath);
                Environment.Exit(-1);
            }
        }

        public static CsvHelper.Configuration.CsvConfiguration GetConfiguration()
        {
            var cfg = new CsvHelper.Configuration.CsvConfiguration();
            cfg.TrimFields = true;
            cfg.TrimHeaders = true;
            cfg.SkipEmptyRecords = false;
            cfg.IgnoreBlankLines = true;
            cfg.IgnoreReadingExceptions = false;
            cfg.ThrowOnBadData = true;
            cfg.WillThrowOnMissingField = false;
            cfg.IgnoreHeaderWhiteSpace = true;
            cfg.HasHeaderRecord = true;
            cfg.IsHeaderCaseSensitive = false;
            cfg.Quote = '"';
            return cfg;
        }

        static void ProcessUntranslatedStrings(Options options, string translationsPath, IEnumerable<string> validResourceFileNames, IEnumerable<string> locales)
        {
            // Go through resource names
            foreach (var resourceName in validResourceFileNames)
            {
                var baselineResources = GetCultureResources(translationsPath, resourceName, s_baselineCulture);

                // Remove any empty keys
                var emptyKeys = (from r in baselineResources where string.IsNullOrEmpty(r.Value) select r.Key).ToList();
                if (emptyKeys.Any())
                {
                    foreach (var key in emptyKeys)
                    {
                        baselineResources.Remove(key);
                    }

                    AddOrUpdateResourceFile(translationsPath, baselineResources, resourceName, s_baselineCulture);
                }

                // Go through valid culture codes
                foreach (var locale in locales)
                {
                    var neutralLocale = CultureHelper.GetNeutralCulture(locale);
                    var localeResources = GetCultureResources(translationsPath, resourceName, locale);

                    // Go through valid resource keys
                    foreach (var baselineResource in baselineResources)
                    {
                        var baselineResourceKey = baselineResource.Key;
                        var baselineResourceValue = baselineResource.Value;

                        // Does the config specify that we ignore this key?
                        if (s_config.Ignore.Contains(baselineResourceValue))
                        {
                            // Let's see if it already exists, and remove it fromt he processing queue if so
                            if (localeResources.ContainsKey(baselineResourceKey))
                            {
                                localeResources.Remove(baselineResourceKey);
                            }

                            // Move on to next key
                            continue;
                        }

                        // If the localized value doesn't exist, or matches the english value, set it to null
                        var localeResourceValue = localeResources.ContainsKey(baselineResourceKey)
                            ? (localeResources[baselineResourceKey] == baselineResourceValue
                                ? null
                                : localeResources[baselineResourceKey])
                            : null;

                        if (string.IsNullOrEmpty(localeResourceValue))
                        {
                            // Do we have any override translations?
                            if (s_config.Custom.ContainsKey(locale) && s_config.Custom[locale].ContainsKey(baselineResourceValue))
                            {
                                localeResourceValue = s_config.Custom[locale][baselineResourceValue];
                            }
                            else
                            {
                                // Fetch from web service using neutral locale (es-419 errors)
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine("Trying web service for '{0}.{1}'", resourceName, baselineResourceKey);
                                Console.ResetColor();
                                localeResourceValue = GetWebTranslation(baselineResourceValue, s_baselineCulture, neutralLocale);
                            }
                        }

                        // Add or update the value
                        if (!localeResources.ContainsKey(baselineResourceKey))
                        {
                            localeResources.Add(baselineResourceKey, localeResourceValue);
                        }
                        else
                        {
                            localeResources[baselineResourceKey] = localeResourceValue;
                        }
                    }

                    // Save changes off to the filesystem
                    AddOrUpdateResourceFile(translationsPath, localeResources, resourceName, locale);
                }
            }
        }

        public static void AddOrUpdateResourceFile(string translationsPath, SortedDictionary<string, string> data, string resourceName, string culture)
        {
            var path = string.Format(Path.Combine(translationsPath, culture == CultureHelper.GetNeutralCulture(s_baselineCulture)
                ? string.Format(BASELINE_RESX_FORMAT, resourceName)
                : string.Format(LOCALIZED_RESX_FORMAT, resourceName, culture)));

            AddOrUpdateResourceFile(data, path);
        }

        public static void AddOrUpdateResourceFile(SortedDictionary<string, string> data, String path)
        {
            if (!path.EndsWith(".resx"))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Path doesn't resolve to a valid .resx file!");
                Console.ResetColor();
                Environment.Exit(-1);
            }

            var resourceEntries = GetResources(path);

            // Modify resources
            foreach (String key in data.Keys)
            {
                String value = data[key];
                if (!resourceEntries.ContainsKey(key))
                {
                    resourceEntries.Add(key, value);
                }
                else
                {
                    resourceEntries[key] = value;
                }
            }

            // Remove old one
            File.Delete(path);

            // Write the combined resource file
            using (ResXResourceWriter resourceWriter = new ResXResourceWriter(path))
            {
                foreach (String key in resourceEntries.Keys)
                {
                    resourceWriter.AddResource(key, resourceEntries[key]);
                }
            }

        }

        private static SortedDictionary<string, string> GetCultureResources(string translationsPath, string resourceFileName, string culture)
        {
            var path = string.Format(Path.Combine(translationsPath, culture == CultureHelper.GetNeutralCulture(s_baselineCulture)
                ? string.Format(BASELINE_RESX_FORMAT, resourceFileName)
                : string.Format(LOCALIZED_RESX_FORMAT, resourceFileName, culture)));

            return GetResources(path);
        }

        private static SortedDictionary<string, string> GetResources(string path)
        {
            var resourceEntries = new SortedDictionary<string, string>();

            // Get existing resources
            if (File.Exists(path))
            {
                using (ResXResourceReader reader = new ResXResourceReader(path))
                {
                    foreach (DictionaryEntry d in reader)
                    {
                        resourceEntries.Add(d.Key.ToString(), d.Value == null ? string.Empty : d.Value.ToString());
                    }
                }
            }

            return resourceEntries;
        }

        private static string GetWebTranslation(string fromText, string fromLocale, string toLocale)
        {
            string result = fromText;

            // Do we have a fresh auth token?
            if (string.IsNullOrEmpty(s_authToken) || s_authExpireDate <= DateTime.UtcNow)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Fetching new bing API auth token... ");
                EnsureAuthToken();
                Console.WriteLine("Done!");
            }

            Console.ResetColor();
            Console.Write("Using web service to translate '");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(fromText);
            Console.ResetColor();
            Console.Write("' to ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(toLocale);
            Console.ResetColor();
            Console.Write("... ");

            // Do the actual translation once we have our access token
            HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(string.Format(BING_TRANSLATE_ENDPOINT_FORMAT, HttpUtility.UrlEncode(fromText), fromLocale, toLocale));
            httpWebRequest.Headers.Add("Authorization", s_authToken);
            WebResponse response = null;
            try
            {
                response = httpWebRequest.GetResponse();
                using (Stream stream = response.GetResponseStream())
                {
                    var dcs = new DataContractSerializer(typeof(string));
                    result = (string)dcs.ReadObject(stream);
                }
            }
            finally
            {
                if (response != null)
                {
                    response.Close();
                }
            }

            Console.WriteLine("Done.");
            Console.Write("Resulting String: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(result);
            Console.ResetColor();

            return result;
        }

        private static void EnsureAuthToken()
        {
            AdmAccessToken admToken;
            //Get Client Id and Client Secret from https://datamarket.azure.com/developer/applications/
            //Refer obtaining AccessToken (http://msdn.microsoft.com/en-us/library/hh454950.aspx) 
            AdmAuthentication admAuth = new AdmAuthentication("ddtech-localizationhelper-1", "Q3D2Y90emP7XDdJysRTB3A9d8tJDFFiUBHuPr5JGTQk=");
            try
            {
                admToken = admAuth.GetAccessToken();

                // Create a header with the access_token property of the returned token
                s_authToken = "Bearer " + admToken.access_token;

                // Add expiration, minus a reasonable buffer for long requests
                s_authExpireDate = DateTime.UtcNow.AddSeconds(int.Parse(admToken.expires_in) - 5);
            }
            catch (WebException e)
            {
                ProcessWebException(e);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static void ProcessWebException(WebException e)
        {
            Console.WriteLine("{0}", e.ToString());

            // Obtain detailed error information
            string strResponse = string.Empty;
            using (HttpWebResponse response = (HttpWebResponse)e.Response)
            {
                using (Stream responseStream = response.GetResponseStream())
                {
                    using (StreamReader sr = new StreamReader(responseStream, System.Text.Encoding.UTF8))
                    {
                        strResponse = sr.ReadToEnd();
                    }
                }
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Http status code={0}, error message={1}", e.Status, strResponse);
            Console.ResetColor();
        }
    }

}