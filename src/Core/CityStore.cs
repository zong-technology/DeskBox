using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using DeskBox.Core;

namespace DeskBox.Core
{
    public class CityInfo
    {
        public long Id = 0;             // Open-Meteo geonames id（唯一标识）
        public string Name = "";
        public string Admin1 = "";      // 省/州
        public string Country = "";
        public double Lat = 0, Lon = 0;
        public string Timezone = "";
        public string Display
        {
            get
            {
                var parts = new List<string>();
                parts.Add(Name);
                if (!string.IsNullOrEmpty(Admin1) && Admin1 != Name) parts.Add(Admin1);
                if (!string.IsNullOrEmpty(Country)) parts.Add(Country);
                return string.Join(" · ", parts.ToArray());
            }
        }
        public string Key { get { return Id > 0 ? "id:" + Id : ("ll:" + Lat.ToString("0.####") + "," + Lon.ToString("0.####")); } }
    }

    public class CityStore
    {
        private readonly List<CityInfo> _favorites = new List<CityInfo>();
        private CityInfo _current;

        public List<CityInfo> Favorites { get { return _favorites.ToList(); } }
        public CityInfo Current { get { return _current; } }
        public string FilePath { get { return Path.Combine(AppPaths.DataDir, "cities.json"); } }

        public CityStore() { Load(); }

        public void Load()
        {
            try
            {
                var dto = Json.Load<CityFile>(FilePath, null);
                if (dto == null) return;
                _favorites.Clear();
                if (dto.Favorites != null) _favorites.AddRange(dto.Favorites.Where(c => c != null && !string.IsNullOrEmpty(c.Name)));
                if (!string.IsNullOrEmpty(dto.CurrentKey))
                    _current = _favorites.FirstOrDefault(c => c.Key == dto.CurrentKey);
                if (_current == null && _favorites.Count > 0) _current = _favorites[0];
            }
            catch (Exception ex) { AppLog.Write("warn", "weather", "读取城市配置失败: " + ex.Message); }
        }

        public void Save()
        {
            try
            {
                Json.Save(FilePath, new CityFile
                {
                    Favorites = _favorites,
                    CurrentKey = _current != null ? _current.Key : ""
                });
            }
            catch (Exception ex) { AppLog.Write("error", "weather", "保存城市配置失败: " + ex.Message); }
        }

        public void SetCurrent(CityInfo city)
        {
            if (city == null) return;
            _current = city;
            if (!_favorites.Any(c => c.Key == city.Key)) _favorites.Add(city);
            Save();
        }

        public void AddFavorite(CityInfo city)
        {
            if (city == null) return;
            if (_favorites.Any(c => c.Key == city.Key)) return;
            _favorites.Add(city);
            Save();
        }

        public void RemoveFavorite(string key)
        {
            _favorites.RemoveAll(c => c.Key == key);
            if (_current != null && _current.Key == key) _current = _favorites.FirstOrDefault();
            Save();
        }

        public class CityFile
        {
            public List<CityInfo> Favorites = new List<CityInfo>();
            public string CurrentKey = "";
        }
    }

    /// <summary>城市搜索（Open-Meteo geocoding，真实接口，返回唯一 id + 经纬度）。</summary>
    public static class CitySearch
    {
        public static List<CityInfo> Search(string keyword, int count)
        {
            var list = new List<CityInfo>();
            if (string.IsNullOrWhiteSpace(keyword)) return list;
            try
            {
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                var url = "https://geocoding-api.open-meteo.com/v1/search?count=" + Math.Max(1, Math.Min(20, count))
                          + "&language=zh&format=json&name=" + Uri.EscapeDataString(keyword.Trim());
                var raw = HttpGet(url, 8000);
                var dto = Json.FromJson<GeoDto>(raw);
                if (dto == null || dto.results == null) return list;
                foreach (var r in dto.results)
                {
                    list.Add(new CityInfo
                    {
                        Id = r.id,
                        Name = r.name ?? "",
                        Admin1 = r.admin1 ?? "",
                        Country = r.country ?? "",
                        Lat = r.latitude,
                        Lon = r.longitude,
                        Timezone = r.timezone ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("warn", "weather", "城市搜索失败: " + ex.Message);
            }
            return list;
        }

        private static string HttpGet(string url, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs;
            req.UserAgent = "DeskBox/3.0";
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        internal class GeoDto
        {
            public List<GeoItem> results { get; set; }
        }

        internal class GeoItem
        {
            public long id { get; set; }
            public string name { get; set; }
            public string admin1 { get; set; }
            public string country { get; set; }
            public double latitude { get; set; }
            public double longitude { get; set; }
            public string timezone { get; set; }
        }
    }

    /// <summary>兼容包装：让上面的 POCO 能被现有 Json（JavaScriptSerializer）序列化。</summary>
    internal static class JavaScriptSerializerCompat
    {
    }
}
