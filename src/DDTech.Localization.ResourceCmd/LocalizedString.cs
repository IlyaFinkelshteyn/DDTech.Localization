using CsvHelper.Configuration;

namespace DDTech.Tools.LocalizationHelper
{
    public class LocalizedString
    {
        public string FileName { get; set; }
        public string FieldName { get; set; }
        public string EnglishString { get; set; }
        public string TranslatedString { get; set; }
    }

    public sealed class LocalizedStringMap : CsvClassMap<LocalizedString>
    {
        public LocalizedStringMap()
        {
            Map(m => m.EnglishString).Index(0);
            Map(m => m.TranslatedString).Index(1);
        }
    }
}
