using System.Collections.Generic;

namespace DDTech.Tools.LocalizationHelper
{
    public class Config
    {
        public Dictionary<string, Dictionary<string, string>> Custom { get; set; }
        public List<string> Ignore { get; set; }
    }
}
