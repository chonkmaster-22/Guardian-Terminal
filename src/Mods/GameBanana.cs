using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace GuardianTerminal.Mods
{
    internal sealed class GbFile
    {
        public long Id;
        public string Name, Url, Scan, Description;
        public long Size;
        public DateTime Added;
    }

    internal sealed class GbMod
    {
        public long Id, GameId;
        public string Name, Game, Version, Description, Submitter, PreviewUrl;
        public List<GbFile> Files = new List<GbFile>();
        public string PageUrl { get { return "https://gamebanana.com/mods/" + Id; } }
        public bool IsBotwSwitch { get { return GameId == GameBanana.BotwSwitchGameId; } }
        public DateTime NewestFile { get { return Files.Count == 0 ? DateTime.MinValue : Files.Max(f => f.Added); } }
    }

    // A mod in GameBanana's lists (browsing, searching)
    internal sealed class GbListing
    {
        public long Id;
        public string Name, Author, Category, Version, ThumbUrl;
        public long Likes, Views;
        public DateTime Added, Updated;
        public bool HasFiles;
        public bool Mature;  // GameBanana's content ratings say it's for adults (or its name says NSFW)
    }

    internal sealed class GbCategory
    {
        public long Id;
        public string Name;
        public long Count;
    }

    internal sealed class GbPage
    {
        public List<GbListing> Items = new List<GbListing>();
        public long Total;
    }

    // GameBanana's public API (apiv11): browsing and searching the Breath of the Wild (Switch) mods, a mod's page by
    // its id, and downloading one of its files.
    internal static class GameBanana
    {
        // the orders the mod list can be browsed in
        public const string SortPopular = "Generic_MostLiked", SortUpdated = "Generic_LatestModified", SortNewest = "";

        public static GbPage Browse(string sort, long category, int page, int perPage)
        {
            string url = "https://gamebanana.com/apiv11/Mod/Index?_nPerpage=" + perPage + "&_aFilters%5BGeneric_Game%5D=" + BotwSwitchGameId +
                (category > 0 ? "&_aFilters%5BGeneric_Category%5D=" + category : "") + "&_nPage=" + page + (string.IsNullOrEmpty(sort) ? "" : "&_sSort=" + sort);
            return ReadPage(url);
        }

        public static GbPage Search(string text, int page, int perPage)
        {
            string url = "https://gamebanana.com/apiv11/Util/Search/Results?_sModelName=Mod&_sOrder=best_match&_idGameRow=" + BotwSwitchGameId +
                "&_sSearchString=" + Uri.EscapeDataString(text) + "&_nPage=" + page + "&_nPerpage=" + perPage;
            return ReadPage(url);
        }

        public static List<GbCategory> Categories()
        {
            object[] list = Net.GetJson("https://gamebanana.com/apiv11/Mod/Categories?_idGameRow=" + BotwSwitchGameId + "&_sSort=a_to_z&_bShowEmpty=false") as object[];
            List<GbCategory> r = new List<GbCategory>();
            if (list == null) return r;
            foreach (Dictionary<string, object> c in list.OfType<Dictionary<string, object>>())
            {
                long id, n;
                if (!long.TryParse(Get(c, "_idRow"), out id)) continue;
                long.TryParse(Get(c, "_nItemCount"), out n);
                r.Add(new GbCategory { Id = id, Name = Get(c, "_sName"), Count = n });
            }
            return r;
        }

        private static GbPage ReadPage(string url)
        {
            Dictionary<string, object> o = Net.GetJson(url) as Dictionary<string, object>;
            GbPage page = new GbPage();
            if (o == null) return page;
            Dictionary<string, object> meta = o.ContainsKey("_aMetadata") ? o["_aMetadata"] as Dictionary<string, object> : null;
            long total;
            page.Total = long.TryParse(Get(meta, "_nRecordCount"), out total) ? total : 0;
            object[] records = o.ContainsKey("_aRecords") ? o["_aRecords"] as object[] : null;
            if (records == null) return page;
            foreach (Dictionary<string, object> r in records.OfType<Dictionary<string, object>>())
            {
                if (Get(r, "_sModelName") != null && Get(r, "_sModelName") != "Mod") continue;
                GbListing m = new GbListing();
                long v;
                m.Id = long.TryParse(Get(r, "_idRow"), out v) ? v : 0;
                if (m.Id == 0) continue;
                m.Name = WebUtility.HtmlDecode(Get(r, "_sName") ?? "");
                m.Version = Get(r, "_sVersion");
                m.Author = Get(r.ContainsKey("_aSubmitter") ? r["_aSubmitter"] as Dictionary<string, object> : null, "_sName");
                m.Category = Get(r.ContainsKey("_aRootCategory") ? r["_aRootCategory"] as Dictionary<string, object> : null, "_sName");
                m.Likes = long.TryParse(Get(r, "_nLikeCount"), out v) ? v : 0;
                m.Views = long.TryParse(Get(r, "_nViewCount"), out v) ? v : 0;
                m.Added = long.TryParse(Get(r, "_tsDateAdded"), out v) ? FromUnix(v) : DateTime.MinValue;
                m.Updated = long.TryParse(Get(r, "_tsDateModified"), out v) ? FromUnix(v) : m.Added;
                m.HasFiles = Get(r, "_bHasFiles") != "False";
                m.Mature = Get(r, "_bHasContentRatings") == "True" || m.Name.IndexOf("nsfw", StringComparison.OrdinalIgnoreCase) >= 0;
                Dictionary<string, object> media = r.ContainsKey("_aPreviewMedia") ? r["_aPreviewMedia"] as Dictionary<string, object> : null;
                object[] images = media != null && media.ContainsKey("_aImages") ? media["_aImages"] as object[] : null;
                Dictionary<string, object> first = images == null ? null : images.OfType<Dictionary<string, object>>().FirstOrDefault();
                if (first != null && Get(first, "_sBaseUrl") != null)
                    m.ThumbUrl = Get(first, "_sBaseUrl").TrimEnd('/') + "/" + (Get(first, "_sFile220") ?? Get(first, "_sFile"));
                page.Items.Add(m);
            }
            return page;
        }

        private static DateTime FromUnix(long seconds)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime();
        }

        public static byte[] GetBytes(string url)
        {
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = Agent;
                return wc.DownloadData(url);
            }
        }
        public const long BotwSwitchGameId = 6386;
        public const string BotwSwitchModsUrl = "https://gamebanana.com/mods/games/6386";
        private const string Agent = "GuardianTerminal/0.4 (+https://github.com/chonkmaster-22/guardian-terminal)";

        static GameBanana()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        // The mod id in "https://gamebanana.com/mods/123456", "gamebanana.com/mods/download/123456" or "123456"; 0: none
        public static long ParseModId(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            Match m = Regex.Match(text, @"gamebanana\.com/mods/(?:download/)?(\d+)", RegexOptions.IgnoreCase);
            if (!m.Success) m = Regex.Match(text.Trim(), @"^(\d+)$");
            long id;
            return m.Success && long.TryParse(m.Groups[1].Value, out id) ? id : 0;
        }

        public static GbMod GetMod(long id)
        {
            string json;
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = Agent;
                wc.Encoding = Encoding.UTF8;
                json = wc.DownloadString("https://gamebanana.com/apiv11/Mod/" + id + "/ProfilePage");
            }
            JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Dictionary<string, object> o = js.DeserializeObject(json) as Dictionary<string, object>;
            if (o == null || Get(o, "_sName") == null) throw new InvalidDataException(L.F("GameBanana didn't return a mod page for {0}.", id));
            GbMod mod = new GbMod();
            mod.Id = id;
            mod.Name = Get(o, "_sName");
            mod.Version = Get(o, "_sVersion");
            mod.Description = Plain(Get(o, "_sText"));
            Dictionary<string, object> game = o.ContainsKey("_aGame") ? o["_aGame"] as Dictionary<string, object> : null;
            mod.Game = Get(game, "_sName");
            long gid;
            mod.GameId = long.TryParse(Get(game, "_idRow"), out gid) ? gid : 0;
            mod.Submitter = Get(o.ContainsKey("_aSubmitter") ? o["_aSubmitter"] as Dictionary<string, object> : null, "_sName");
            // its first preview picture: _aPreviewMedia._aImages[0], at 530 px wide when there is that size
            Dictionary<string, object> media = o.ContainsKey("_aPreviewMedia") ? o["_aPreviewMedia"] as Dictionary<string, object> : null;
            object[] images = media != null && media.ContainsKey("_aImages") ? media["_aImages"] as object[] : null;
            Dictionary<string, object> first = images == null ? null : images.OfType<Dictionary<string, object>>().FirstOrDefault();
            if (first != null && Get(first, "_sBaseUrl") != null)
                mod.PreviewUrl = Get(first, "_sBaseUrl").TrimEnd('/') + "/" + (Get(first, "_sFile530") ?? Get(first, "_sFile"));
            object[] files = o.ContainsKey("_aFiles") ? o["_aFiles"] as object[] : null;
            if (files != null)
                foreach (Dictionary<string, object> f in files.OfType<Dictionary<string, object>>())
                {
                    GbFile g = new GbFile();
                    long v;
                    g.Id = long.TryParse(Get(f, "_idRow"), out v) ? v : 0;
                    g.Name = Get(f, "_sFile");
                    g.Url = Get(f, "_sDownloadUrl");
                    g.Size = long.TryParse(Get(f, "_nFilesize"), out v) ? v : 0;
                    g.Description = Get(f, "_sDescription");
                    g.Scan = Get(f, "_sAnalysisResult") ?? Get(f, "_sAnalysisState");
                    g.Added = long.TryParse(Get(f, "_tsDateAdded"), out v) ? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(v).ToLocalTime() : DateTime.MinValue;
                    if (!string.IsNullOrEmpty(g.Url)) mod.Files.Add(g);
                }
            return mod;
        }

        // Downloads a file of a mod into folder; returns its path. progress(bytes so far, total or -1)
        public static string Download(GbFile f, string folder, Action<long, long> progress)
        {
            Directory.CreateDirectory(folder);
            string name = string.Concat((f.Name ?? ("gamebanana-" + f.Id)).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string path = Path.Combine(folder, name), tmp = path + ".part";
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(f.Url);
            req.UserAgent = Agent;
            req.AllowAutoRedirect = true;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (FileStream o = File.Create(tmp))
            {
                long total = resp.ContentLength > 0 ? resp.ContentLength : f.Size > 0 ? f.Size : -1, done = 0;
                byte[] buf = new byte[1 << 16];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    o.Write(buf, 0, n);
                    done += n;
                    progress(done, total);
                }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            return path;
        }

        // A mod's preview picture saved as thumbnails\<name>.jpg (its file name returned), or null
        public static string SaveThumbnail(GbMod mod, string name)
        {
            if (string.IsNullOrEmpty(mod.PreviewUrl)) return null;
            string file = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + Path.GetExtension(mod.PreviewUrl.Split('?')[0]);
            if (Path.GetExtension(file).Length == 0) file += ".jpg";
            Net.Download(mod.PreviewUrl, Path.Combine(AppPaths.Thumbnails, file), null);
            return file;
        }

        // The mod id of a GameBanana page link (a mod's SourceUrl), or 0
        public static long IdOf(string url)
        {
            return url == null ? 0 : ParseModId(url);
        }

        private static string Get(Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : null;
        }

        private static string Plain(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            string t = Regex.Replace(html, @"<br\s*/?>|</p>|</li>", "\n", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "<[^>]+>", " ");
            t = WebUtility.HtmlDecode(t);
            t = Regex.Replace(t, @"[ \t]+", " ");
            t = Regex.Replace(t, @"\n\s*\n+", "\n\n");
            return t.Trim();
        }
    }
}
