const fs = require('fs');
const p = 'src/Core/Weather.cs';
let s = fs.readFileSync(p, 'utf8');

const model = `    public class HourlyPoint
    {
        public string Time = "";
        public string Text = "";
        public string Icon = "";
        public double Temp = 0;
        public int PrecipProb = 0;
    }

    public class WeatherSnapshot`;

if (!s.includes('class HourlyPoint')) {
  s = s.replace('    public class WeatherSnapshot', model);
}
if (!s.includes('public List<HourlyPoint> Hours')) {
  s = s.replace('        public List<WeatherDay> Days = new List<WeatherDay>();',
    '        public List<WeatherDay> Days = new List<WeatherDay>();\n        public List<HourlyPoint> Hours = new List<HourlyPoint>();');
}

// 请求里加 hourly
s = s.replace('"&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max",',
  '"&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max" +\n                "&hourly=temperature_2m,weather_code,precipitation_probability&forecast_hours=24",');

// 解析 hourly（在解析 daily 之后插入）
const parseAnchor = `            if (snap.Days.Count == 0) snap.Days = FallbackDays();`;
const hourlyParse = `            object hourlyObj;
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

            if (snap.Days.Count == 0) snap.Days = FallbackDays();`;
if (!s.includes('hourlyObj')) {
  s = s.replace(parseAnchor, hourlyParse);
}

fs.writeFileSync(p, s, 'utf8');
console.log('hourly:', s.includes('hourlyObj'), '| model:', s.includes('class HourlyPoint'), '| field:', s.includes('List<HourlyPoint> Hours'));
