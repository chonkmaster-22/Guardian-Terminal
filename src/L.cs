using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace GuardianTerminal
{
    // Guardian Terminal in the player's language. T("English text") is its translation in the language in use (the
    // English itself when there's none), F("... {0} ...", args) the same with string.Format, P(n, one, many) picks the
    // singular or plural. The code reads in English: Lang\Keys.cs lists every text (tools\l10n.ps1 makes it from the
    // source) and each language has one translation per text, in the same order. A missing or broken translation just
    // shows English. Logs (the merge report, crash.txt) stay in English.
    internal static class L
    {
        public sealed class Language
        {
            public string Code, Name;            // "es", "Español"
            public Func<string[]> Texts;         // its translations of Lang.Keys.All, line for line (none: English)
        }

        public static readonly List<Language> All = new List<Language>
        {
            new Language { Code = "en", Name = "English" },
            new Language { Code = "es", Name = "Español", Texts = () => Lang.Spanish.Texts },
            new Language { Code = "fr", Name = "Français", Texts = () => Lang.French.Texts },
            new Language { Code = "de", Name = "Deutsch", Texts = () => Lang.German.Texts },
            new Language { Code = "it", Name = "Italiano", Texts = () => Lang.Italian.Texts },
            new Language { Code = "pt", Name = "Português (Brasil)", Texts = () => Lang.Portuguese.Texts },
            new Language { Code = "ja", Name = "日本語", Texts = () => Lang.Japanese.Texts },
            new Language { Code = "zh", Name = "简体中文", Texts = () => Lang.Chinese.Texts },
        };

        private static Dictionary<string, string> table;

        public static string Code { get; private set; }

        static L()
        {
            Code = "en";
        }

        // "auto" (or null): Windows' display language when it's one of these, else English
        public static void Use(string code)
        {
            if (string.IsNullOrEmpty(code) || code == "auto") code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            Language lang = All.FirstOrDefault(l => l.Code == code) ?? All[0];
            Code = lang.Code;
            table = null;
            try
            {
                string[] texts = lang.Texts == null ? null : lang.Texts();
                string[] keys = Lang.Keys.All;
                if (texts == null || texts.Length > keys.Length) return;  // (more lines than texts: out of line, so none)
                table = new Dictionary<string, string>(texts.Length, StringComparer.Ordinal);
                for (int i = 0; i < texts.Length; i++)
                    if (!string.IsNullOrEmpty(texts[i]) && Holes(texts[i]) == Holes(keys[i])) table[keys[i]] = texts[i];
            }
            catch (Exception) { table = null; }
        }

        // "0,1" for a text with {0} and {1}: a translation must have the same, or English is shown
        private static string Holes(string s)
        {
            return string.Join(",", Regex.Matches(s, @"\{(\d+)").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x, StringComparer.Ordinal));
        }

        public static string T(string english)
        {
            string t;
            return english != null && table != null && table.TryGetValue(english, out t) && !string.IsNullOrEmpty(t) ? t : english;
        }

        public static string F(string english, params object[] args)
        {
            try { return string.Format(T(english), args); }
            catch (FormatException) { return string.Format(english, args); }  // (a translation with a broken {n})
        }

        // n with the singular or the plural ("{0} mod" / "{0} mods"), translated; more args are {1}, {2}...
        public static string P(long n, string one, string many, params object[] args)
        {
            object[] all = new object[args.Length + 1];
            all[0] = n;
            args.CopyTo(all, 1);
            return F(n == 1 ? one : many, all);
        }

        // A mod's note as it's kept in its mod.json: the English text, or the English template and its values
        // separated by \u001F (Note.Make), so it's shown in the language in use whenever it's read
        public static string Note(string kept)
        {
            if (kept == null) return "";
            string[] parts = kept.Split('\u001F');
            if (parts.Length == 1) return T(kept);
            // (a value that's a list of English words, like "sound links, HUD layout", is translated word by word;
            // numbers and file names have no translation and stay as they are)
            return F(parts[0], parts.Skip(1).Select(v => (object)string.Join(", ", v.Split(new[] { ", " }, StringSplitOptions.None).Select(T))).ToArray());
        }

        public static string MakeNote(string template, params object[] args)
        {
            return args.Length == 0 ? template : template + "\u001F" + string.Join("\u001F", args.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture)));
        }

        // the English template of a kept note (to tell what kind of note it is)
        public static string NoteKey(string kept)
        {
            if (kept == null) return "";
            int i = kept.IndexOf('\u001F');
            return i < 0 ? kept : kept.Substring(0, i);
        }
    }
}
