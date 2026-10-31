using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace GuardianTerminal
{
    internal static class Json
    {
        private static JavaScriptSerializer Serializer()
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            return s;
        }

        // null when the file is missing or unreadable
        public static T Read<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            try { return Serializer().Deserialize<T>(File.ReadAllText(path, Encoding.UTF8)); }
            catch (Exception) { return null; }
        }

        public static Dictionary<string, object> ReadObject(string path)
        {
            if (!File.Exists(path)) return null;
            try { return Serializer().DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>; }
            catch (Exception) { return null; }
        }

        // Writes through a temporary file so a crash never leaves half a file behind.
        public static void Write(string path, object value)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Indent(Serializer().Serialize(value)), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        public static string GetString(Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : null;
        }

        private static string Indent(string json)
        {
            StringBuilder sb = new StringBuilder(json.Length * 2);
            int depth = 0;
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) sb.Append(json[++i]);
                    else if (c == '"') inString = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inString = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(json[++i]); break; }
                        depth++;
                        sb.Append('\n').Append(' ', depth * 2);
                        break;
                    case '}':
                    case ']':
                        depth--;
                        sb.Append('\n').Append(' ', depth * 2).Append(c);
                        break;
                    case ',': sb.Append(",\n").Append(' ', depth * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.Append('\n').ToString();
        }
    }
}
