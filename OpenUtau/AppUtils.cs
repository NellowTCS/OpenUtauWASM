using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Styling;
using OpenUtau.Core.Util;

namespace OpenUtau.App {
    public static class AppUtils {
        public static Dictionary<string, dynamic> GetLanguages() {
            var result = new Dictionary<string, dynamic>();
            if (Application.Current == null) {
                return result;
            }
            foreach (string key in Application.Current.Resources.Keys.OfType<string>()) {
                if (key.StartsWith("strings-") &&
                    Application.Current.Resources.TryGetResource(key, ThemeVariant.Default, out var res) &&
                    res != null) {
                    result.Add(key.Replace("strings-", ""), res);
                }
            }
            return result;
        }

        public static void SetLanguage(string language) {
            if (Application.Current == null) {
                return;
            }
            var languages = GetLanguages();
            foreach (var res in languages.Values) {
                Application.Current.Resources.MergedDictionaries.Remove(res);
            }
            if (language != "en-US") {
                Application.Current.Resources.MergedDictionaries.Add(languages["en-US"]);
            }
            if (languages.TryGetValue(language, out var res1)) {
                Application.Current.Resources.MergedDictionaries.Add(res1);
            }
        }

        public static void SetTheme() {
            if (Application.Current == null) {
                return;
            }
            dynamic? light = Application.Current.Resources["themes-light"];
            dynamic? dark = Application.Current.Resources["themes-dark"];
            dynamic? custom = Application.Current.Resources["themes-custom"];
            switch (Preferences.Default.ThemeName) { 
                case "Light":
                    if (light != null) ApplyTheme(light);
                    Application.Current.RequestedThemeVariant = ThemeVariant.Light;
                    break;
                case "Dark":
                    if (dark != null) ApplyTheme(dark);
                    Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
                    break;
                default:
                    if (custom != null) ApplyTheme(custom);
                    Colors.CustomTheme.ApplyTheme(Preferences.Default.ThemeName);
                    if (Colors.CustomTheme.Default.IsDarkMode == true) {
                        Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
                    } else {
                        Application.Current.RequestedThemeVariant = ThemeVariant.Light;
                    }
                    break;
            }
            ThemeManager.LoadTheme();
        }

        private static void ApplyTheme(dynamic resDict) { 
            var res = Application.Current?.Resources;
            foreach (var item in resDict) {
                res![item.Key] = item.Value;
            }
        }
    }
}
