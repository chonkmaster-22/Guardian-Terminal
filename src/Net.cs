using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace GuardianTerminal
{
    // Web requests: GameBanana, GitHub's releases (Savage Wind's and Guardian Terminal's own). Nothing about the
    // player is sent: only the request itself, with this app's name as the user agent (GitHub's API requires one).
    internal static class Net
    {
        public const string UserAgent = "GuardianTerminal/0.4 (+https://github.com/chonkmaster-22/guardian-terminal)";

        static Net()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        public static string GetString(string url)
        {
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                if (url.StartsWith("https://api.github.com/", StringComparison.Ordinal)) wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                wc.Encoding = Encoding.UTF8;
                return wc.DownloadString(url);
            }
        }

        public static object GetJson(string url)
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(GetString(url));
        }

        // Downloads to path (through path.part, so a broken download never looks finished). progress(done, total or -1)
        public static void Download(string url, string path, Action<long, long> progress)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".part";
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UserAgent;
            req.AllowAutoRedirect = true;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (FileStream o = File.Create(tmp))
            {
                long total = resp.ContentLength > 0 ? resp.ContentLength : -1, done = 0;
                byte[] buf = new byte[1 << 16];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    o.Write(buf, 0, n);
                    done += n;
                    if (progress != null) progress(done, total);
                }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static string Get(Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : null;
        }

        // "0.3.1-alpha.3" > "0.3.0-alpha.3" > "0.2.0": the numbers in them, in order
        public static int CompareVersions(string a, string b)
        {
            List<long> x = Numbers(a), y = Numbers(b);
            for (int i = 0; i < Math.Max(x.Count, y.Count); i++)
            {
                long p = i < x.Count ? x[i] : 0, q = i < y.Count ? y[i] : 0;
                if (p != q) return p.CompareTo(q);
            }
            return 0;
        }

        private static List<long> Numbers(string s)
        {
            List<long> r = new List<long>();
            if (s == null) return r;
            long cur = -1;
            foreach (char c in s)
            {
                if (char.IsDigit(c)) cur = (cur < 0 ? 0 : cur * 10) + (c - '0');
                else if (cur >= 0) { r.Add(cur); cur = -1; }
            }
            if (cur >= 0) r.Add(cur);
            return r;
        }
    }
}
