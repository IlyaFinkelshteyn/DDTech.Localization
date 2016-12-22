using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace DDTech.Localization
{
    public static class CultureHelper
    {
        // Include ONLY cultures you are implementing
        public static readonly List<string> SupportedCultures = new List<string> {
            "en",  // First is the default: English NEUTRAL
            "en-GB", // English (UK)
            "de", // German NEUTRAL
            "es", // Spanish NEUTRAL
            "es-419", // Spanish (Latin America and Caribbean region)
            "fi", // Finnish NEUTRAL
            "fr", // French NEUTRAL
            "hu", // Hungarian NEUTRAL
            "ko", // Korean NEUTRAL
            "pt-PT", //  Portuguese Portugal
            "tr", // Turkish NEUTRAL
            "ru" // Russian NEUTRAL
        }
        // then peel off any that aren't supported by the OS (es-419 anyone?)
        .Intersect(CultureInfo.GetCultures(CultureTypes.AllCultures).Select(c => c.Name)).ToList();

        /// <summary>
        /// Returns true if the language is a right-to-left language. Otherwise, false.
        /// </summary>
        public static bool IsRighToLeft()
        {
            return Thread.CurrentThread.CurrentUICulture.TextInfo.IsRightToLeft;
        }

        /// <summary>
        /// All derivatives of all SupportedCultures (eg. "en-US" is a derivitive of the supported culture "en")
        /// </summary>
        /// <returns>All derivatives of all SupportedCultures</returns>
        public static IEnumerable<CultureInfo> GetSupportedCulturesDerivatives()
        {
            var cultures =
                CultureInfo.GetCultures(CultureTypes.AllCultures)
                    .Where(o => SupportedCultures.Contains(
                        o.TwoLetterISOLanguageName,
                        StringComparer.OrdinalIgnoreCase));
            return cultures;
        }

        /// <summary>
        /// Returns a valid culture name based on "name" parameter. If "name" is not valid, it returns the default culture "en-US"
        /// </summary>
        /// <param name="name">Culture's name (e.g. en-US)</param>
        public static string GetImplementedCulture(string name)
        {
            // make sure it's not null
            if (string.IsNullOrEmpty(name))
            {
                // return Default culture
                return GetDefaultCulture();
            }

            // make sure it is a valid culture first
            if (!SupportedCultures.Any(c => c.Equals(name, StringComparison.InvariantCultureIgnoreCase)))
            {
                // return Default culture if it is invalid
                return GetDefaultCulture();
            }

            // if it is implemented, accept it
            if (SupportedCultures.Any(c => c.Equals(name, StringComparison.InvariantCultureIgnoreCase)))
            {
                // accept it
                return name;
            }

            // Find a close match. For example, if you have "en-US" defined and the user requests "en-GB", 
            // the function will return closes match that is "en-US" because at least the language is the same (ie English)  
            var n = GetNeutralCulture(name);
            foreach (var c in SupportedCultures)
            {
                if (c.StartsWith(n))
                {
                    return c;
                }
            }

            // It is not implemented
            return GetDefaultCulture(); // return Default culture as no match found
        }

        /// <summary>
        /// Returns default culture name which is the first name decalared (e.g. en-US)
        /// </summary>
        /// <returns></returns>
        public static string GetDefaultCulture()
        {
            return SupportedCultures[0]; // return Default culture
        }

        public static string GetCurrentUICulture()
        {
            return Thread.CurrentThread.CurrentUICulture.Name;
        }

        public static string GetCurrentNeutralUICulture()
        {
            return GetNeutralCulture(Thread.CurrentThread.CurrentUICulture.Name);
        }

        public static string GetNeutralCulture(string name)
        {
            if (!name.Contains("-")) return name;

            return name.Split('-')[0]; // Read first part only. E.g. "en", "es"
        }
    }
}