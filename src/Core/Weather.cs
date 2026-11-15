using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace DeskBox
{
    public class WeatherDay
    {
        public string Date = "";
        public string Text = "";
        public string Icon = "❓";
        public double TMax = 0, TMin = 0;
        public int PrecipProb = 0;
        public string DateShort { get { try { return DateTime.Parse(Date).ToString("M/d"); } catch { return Date; } } }
        public string Weekday
        {
            get
            {
                try
                {
                    var d = DateTime.Parse(Date);
                    if (d.Date == DateTime.Today) return "今天";
                    if (d.Date == DateTime.Today.AddDays(1)) return "明天";
                    string[] w = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
                    return w[(int)d.DayOfWeek];
                }
                catch { return ""; }
            }
        }
    }

    public class HourlyPoint
    {
        public string Time = "";
        public string Text = "";
        public string Icon = "";
        public double Temp = 0;
        public int PrecipProb = 0;
    }

    public class WeatherSnapshot
    {
        public string City = "—";
        public string Region = "";
        public string Country = "";
        public double Lat = 0, Lon = 0;
        public double Temp = 0, Feels = 0;
        public int Humidity = 0, Code = 0, PrecipProb = 0;
        public double WindSpeed = 0;
        public string WindDir = "";
        public bool IsDay = true;
        public string Text = "—";
        public string Icon = "❓";
        public string Source = "fallback";     // online | cache | fallback
        public long UpdatedAt = 0;
        public List<WeatherDay> Days = new List<WeatherDay>();
        public List<HourlyPoint> Hours = new List<HourlyPoint>();

        public string UpdatedText
        {
            get
            {
                if (UpdatedAt <= 0) return "";
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAt).LocalDateTime;
                var span = DateTime.Now - dt;
                if (span.TotalMinutes < 1) return "刚刚更新";
                if (span.TotalMinutes < 60) return (int)span.TotalMinutes + " 分钟前更新";
                return dt.ToString("HH:mm") + " 更新";
            }
        }
    }

    /// <summary>Open-Meteo 天气（免 Key）+ 10 分钟缓存 + 断网降级</summary>
    public static class WeatherService
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        private static WeatherSnapshot _cache;
        private static long _cacheAt = 0;
        private static readonly object _lock = new object();

        public static WeatherSnapshot Get(AppSettings s, bool force)
        {
            lock (_lock)
            {
                int refreshMinutes = s == null ? 10 : Math.Max(1, s.WeatherRefreshMinutes);
                if (!force && _cache != null && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _cacheAt < refreshMinutes * 60L * 1000L)
                    return _cache;
                var cached = LoadDiskCache(refreshMinutes);
                if (!force && cached != null)
                {
                    _cache = cached; _cacheAt = cached.UpdatedAt;
                    return _cache;
                }
                WeatherSnapshot snap = null;
                try { snap = FetchOnline(s); } catch (Exception ex) { AppLog.Write("warn", "system", "天气抓取失败: " + ex.Message); }
                if (snap == null) snap = LoadDiskCache(24 * 60);
                if (snap == null) snap = Fallback(s);
                _cache = snap;
                _cacheAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                SaveDiskCache(snap);
                return snap;
            }
        }

        private static string Get(string url, int timeoutMs)
        {
            // 现代 Open-Meteo 只接受 TLS1.2+；.NET 4.8 默认可能仍是 SSL3/TLS1.0
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            ServicePointManager.DefaultConnectionLimit = 8;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "DeskBox/1.0 (+https://localhost)";
            // 不要开启 AutomaticDecompression：部分代理会声明 gzip 但回传明文，导致 DeflateStream 报“块长度不匹配”
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        private static WeatherSnapshot FetchOnline(AppSettings s)
        {
            double lat = s.WeatherLat, lon = s.WeatherLon;
            bool hasCoords = s.HasCoords && !double.IsNaN(lat) && !double.IsNaN(lon);
            // 城市名以用户配置为准，不能固定成某个城市（规格 6.3.9）
            string city = string.IsNullOrWhiteSpace(s.WeatherCity) ? "上海" : s.WeatherCity.Trim();
            string region = "", country = "";
            string src = "online";

            if (double.IsNaN(lat) || double.IsNaN(lon))
            {
                if (!string.IsNullOrWhiteSpace(s.WeatherCity))
                {
                    var geoUrl = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=zh&format=json&name=" +
                                 Uri.EscapeDataString(s.WeatherCity.Trim());
                    var gj = Json.Deserialize<Dictionary<string, object>>(Get(geoUrl, 8000));
                    object resultsObj;
                    if (gj != null && gj.TryGetValue("results", out resultsObj) && resultsObj is object[])
                    {
                        var arr = (object[])resultsObj;
                        if (arr.Length > 0)
                        {
                            var r = (Dictionary<string, object>)arr[0];
                            lat = ToD(r, "latitude"); lon = ToD(r, "longitude");
                            city = ToS(r, "name", city);
                            region = ToS(r, "admin1", "");
                            country = ToS(r, "country", "");
                            src = "online";
                        }
                    }
                }
                if (double.IsNaN(lat) || double.IsNaN(lon) || (lat == 0 && lon == 0))
                {
                    // 首次启动且没有任何配置时的默认位置（仅坐标，不做城市名伪造）
                    lat = 31.2222; lon = 121.4581;
                    if (string.IsNullOrWhiteSpace(city)) city = "上海";
                    AppLog.Write("info", "weather", "未配置城市，使用默认坐标 " + lat + "," + lon);
                }
            }

            var url = string.Format(
                "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&timezone=auto&forecast_days=6" +
                "&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,wind_speed_10m,wind_direction_10m,precipitation_probability" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
                "&hourly=temperature_2m,weather_code,precipitation_probability&forecast_hours=24",
                lat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                lon.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var root = Json.Deserialize<Dictionary<string, object>>(Get(url, 9000));
            if (root == null) return null;

            var snap = new WeatherSnapshot
            {
                City = city, Region = region, Country = country, Lat = lat, Lon = lon,
                Source = src,
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            object curObj;
            if (root.TryGetValue("current", out curObj))
            {
                var cur = (Dictionary<string, object>)curObj;
                snap.Temp = ToD(cur, "temperature_2m");
                snap.Feels = ToD(cur, "apparent_temperature");
                snap.Humidity = (int)ToD(cur, "relative_humidity_2m");
                snap.Code = (int)ToD(cur, "weather_code");
                snap.WindSpeed = ToD(cur, "wind_speed_10m");
                snap.WindDir = DirText(ToD(cur, "wind_direction_10m"));
                snap.IsDay = ToD(cur, "is_day") > 0.5;
                snap.PrecipProb = (int)ToD(cur, "precipitation_probability");
                snap.Text = Describe(snap.Code);
                snap.Icon = Emoji(snap.Code, snap.IsDay);
            }

            object dailyObj;
            if (root.TryGetValue("daily", out dailyObj))
            {
                var d = (Dictionary<string, object>)dailyObj;
                var dates = ToList(d, "time");
                var codes = ToList(d, "weather_code");
                var tmax = ToList(d, "temperature_2m_max");
                var tmin = ToList(d, "temperature_2m_min");
                var prob = ToList(d, "precipitation_probability_max");
                for (int i = 0; i < dates.Count; i++)
                {
                    int code = i < codes.Count ? (int)codes[i] : 0;
                    snap.Days.Add(new WeatherDay
                    {
                        Date = dates[i].ToString(),
                        Text = Describe(code),
                        Icon = Emoji(code, true),
                        TMax = i < tmax.Count ? Dbl(tmax[i]) : 0,
                        TMin = i < tmin.Count ? Dbl(tmin[i]) : 0,
                        PrecipProb = i < prob.Count ? (int)Dbl(prob[i]) : 0
                    });
                }
            }
            object hourlyObj;
            if (root.TryGetValue("hourly", out hourlyObj))
            {
                var h = (Dictionary<string, object>)hourlyObj;
                var times = ToList(h, "time");
                var hcodes = ToList(h, "weather_code");
                var htemps = ToList(h, "temperature_2m");
                var hprob = ToList(h, "precipitation_probability");
                for (int i = 0; i < times.Count && i < 24; i++)
                {
                    int code = i < hcodes.Count ? (int)Dbl(hcodes[i]) : 0;
                    string t = Convert.ToString(times[i]).Replace("T", " ");
                    if (t.Length >= 16) t = t.Substring(11, 5);
                    snap.Hours.Add(new HourlyPoint
                    {
                        Time = t,
                        Text = Describe(code),
                        Icon = Emoji(code, true),
                        Temp = i < htemps.Count ? Dbl(htemps[i]) : 0,
                        PrecipProb = i < hprob.Count ? (int)Dbl(hprob[i]) : 0
                    });
                }
            }

            if (snap.Days.Count == 0) snap.Days = FallbackDays();
            return snap;
        }

        private static WeatherSnapshot Fallback(AppSettings s)
        {
            var now = DateTime.Now;
            double baseTemp = 16 + 9 * Math.Sin((now.Month - 3) / 12.0 * Math.PI * 2);
            var snap = new WeatherSnapshot
            {
                City = string.IsNullOrWhiteSpace(s.WeatherCity) ? "本地" : s.WeatherCity,
                Region = "离线示意",
                Country = "",
                Lat = s.HasCoords ? s.WeatherLat : 31.2222,
                Lon = s.HasCoords ? s.WeatherLon : 121.4581,
                Temp = Math.Round(baseTemp, 1),
                Feels = Math.Round(baseTemp - 0.6, 1),
                Humidity = 62,
                Code = 3,
                Text = "多云（离线示意）",
                Icon = "⛅",
                IsDay = now.Hour >= 6 && now.Hour < 18,
                WindSpeed = 8.5,
                WindDir = "东南",
                PrecipProb = 20,
                Source = "fallback",
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            snap.Days = FallbackDays();
            return snap;
        }

        private static List<WeatherDay> FallbackDays()
        {
            var list = new List<WeatherDay>();
            for (int i = 0; i < 6; i++)
            {
                var d = DateTime.Today.AddDays(i);
                list.Add(new WeatherDay
                {
                    Date = d.ToString("yyyy-MM-dd"),
                    Text = "多云",
                    Icon = "⛅",
                    TMax = 22 - i,
                    TMin = 14 - i,
                    PrecipProb = 20
                });
            }
            return list;
        }

        // ---------- 磁盘缓存 ----------
        private static string CacheFile { get { return Path.Combine(Store.DataDir, "cache", "weather.json"); } }

        private static void SaveDiskCache(WeatherSnapshot s)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CacheFile));
                File.WriteAllText(CacheFile, Json.Serialize(s), new UTF8Encoding(false));
            }
            catch { }
        }

        private static WeatherSnapshot LoadDiskCache(int maxAgeMinutes)
        {
            try
            {
                if (!File.Exists(CacheFile)) return null;
                var s = Json.Deserialize<WeatherSnapshot>(File.ReadAllText(CacheFile, Encoding.UTF8));
                if (s == null) return null;
                // 只认在线结果；离线示意不进缓存，避免断网一次后一直显示“离线示意”
                if (!string.Equals(s.Source, "online", StringComparison.OrdinalIgnoreCase)) return null;
                if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - s.UpdatedAt > Math.Max(1, maxAgeMinutes) * 60L * 1000L) return null;
                s.Source = "cache";
                return s;
            }
            catch { return null; }
        }

        // ---------- 工具 ----------
        private static double ToD(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return 0;
            double r;
            if (v is double) return (double)v;
            if (v is int) return (int)v;
            if (v is long) return (long)v;
            return double.TryParse(Convert.ToString(v), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out r) ? r : 0;
        }

        private static double Dbl(object v)
        {
            if (v == null) return 0;
            if (v is double) return (double)v;
            if (v is int) return (int)v;
            if (v is long) return (long)v;
            double r;
            return double.TryParse(Convert.ToString(v), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out r) ? r : 0;
        }

        private static string ToS(Dictionary<string, object> d, string key, string def)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return def;
            return Convert.ToString(v);
        }

        private static List<object> ToList(Dictionary<string, object> d, string key)
        {
            // JavaScriptSerializer 把 JSON 数组反序列化成 ArrayList，也可能是 object[]，两种都要支持
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return new List<object>();
            var arr = v as object[];
            if (arr != null) return arr.ToList();
            var list = v as System.Collections.IEnumerable;
            if (list != null && !(v is string))
            {
                var result = new List<object>();
                foreach (var item in list) result.Add(item);
                return result;
            }
            return new List<object>();
        }

        public static string DirText(double deg)
        {
            string[] dirs = { "北", "东北", "东", "东南", "南", "西南", "西", "西北" };
            int i = (int)Math.Round(((deg % 360) + 360) % 360 / 45.0) % 8;
            return dirs[i];
        }

        public static string Describe(int code)
        {
            if (code == 0) return "晴";
            if (code == 1) return "晴间多云";
            if (code == 2) return "多云";
            if (code == 3) return "阴";
            if (code == 45 || code == 48) return "雾";
            if (code >= 51 && code <= 57) return "毛毛雨";
            if (code >= 61 && code <= 65) return "雨";
            if (code == 66 || code == 67) return "冻雨";
            if (code >= 71 && code <= 77) return "雪";
            if (code >= 80 && code <= 82) return "阵雨";
            if (code == 85 || code == 86) return "阵雪";
            if (code == 95) return "雷阵雨";
            if (code == 96 || code == 99) return "雷暴伴冰雹";
            return "未知";
        }

        public static string Emoji(int code, bool isDay)
        {
            if (code == 0) return isDay ? "☀️" : "🌙";
            if (code == 1) return isDay ? "🌤" : "🌙";
            if (code == 2) return "⛅";
            if (code == 3) return "☁️";
            if (code == 45 || code == 48) return "🌫";
            if (code >= 51 && code <= 57) return "🌦";
            if (code >= 61 && code <= 67) return "🌧";
            if (code >= 71 && code <= 77) return "❄️";
            if (code >= 80 && code <= 82) return "🌦";
            if (code == 85 || code == 86) return "🌨";
            if (code >= 95) return "⛈";
            return "🌡";
        }
    }
}
