using System;
using System.Collections.Generic;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace MapClash
{
    public sealed class GameText
    {
        private readonly Dictionary<string, LocalizedString> _strings = new Dictionary<string, LocalizedString>();

        public string CountryName(CountryState country)
        {
            return country.IsPlayer ? country.Name : Get("country.bot." + (country.Id - 1));
        }

        public string Get(string key, params object[] arguments)
        {
            if (!_strings.TryGetValue(key, out var localized))
            {
                localized = new LocalizedString("Game", key);
                _strings.Add(key, localized);
            }

            // UI locale callbacks can run before cached LocalizedStrings process the same change.
            localized.LocaleOverride = LocalizationSettings.SelectedLocale;
            return arguments.Length == 0
                ? localized.GetLocalizedString()
                : localized.GetLocalizedString(arguments);
        }
    }
}
