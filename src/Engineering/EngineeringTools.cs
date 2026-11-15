using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DeskBox.Engineering
{
    public class ScaleResult
    {
        public bool Ok;
        public double Value;
        public double Percent;
        public string Error = "";
    }

    public class S7AddressResult
    {
        public bool Ok;
        public string Area = "";
        public int DbNumber = -1;
        public int ByteOffset;
        public int BitOffset = -1;
        public int SizeBits;
        public string Canonical = "";
        public string Description = "";
        public string Error = "";
    }

    /// <summary>自动化工程常用的确定性计算，不依赖网络或 AI。</summary>
    public static class EngineeringTools
    {
        public static ScaleResult FromCurrent(double milliamp, double low, double high)
        {
            if (high == low) return new ScaleResult { Error = "量程上限不能等于下限" };
            var percent = (milliamp - 4.0) / 16.0 * 100.0;
            return new ScaleResult { Ok = true, Percent = percent, Value = low + (high - low) * percent / 100.0 };
        }

        public static ScaleResult ToCurrent(double value, double low, double high)
        {
            if (high == low) return new ScaleResult { Error = "量程上限不能等于下限" };
            var percent = (value - low) / (high - low) * 100.0;
            return new ScaleResult { Ok = true, Percent = percent, Value = 4.0 + 16.0 * percent / 100.0 };
        }

        public static double ConvertPressure(double value, string from, string to)
        {
            var pa = value * PressureFactor(from);
            return pa / PressureFactor(to);
        }

        private static double PressureFactor(string unit)
        {
            switch ((unit ?? "").Trim().ToLowerInvariant())
            {
                case "pa": return 1.0;
                case "kpa": return 1000.0;
                case "mpa": return 1000000.0;
                case "bar": return 100000.0;
                case "mbar": return 100.0;
                case "psi": return 6894.757293168;
                case "kgf/cm²": case "kgf/cm2": return 98066.5;
                default: throw new ArgumentException("不支持的压力单位：" + unit);
            }
        }

        public static double ConvertTemperature(double value, string from, string to)
        {
            double c;
            switch ((from ?? "C").ToUpperInvariant())
            {
                case "F": c = (value - 32.0) * 5.0 / 9.0; break;
                case "K": c = value - 273.15; break;
                default: c = value; break;
            }
            switch ((to ?? "C").ToUpperInvariant())
            {
                case "F": return c * 9.0 / 5.0 + 32.0;
                case "K": return c + 273.15;
                default: return c;
            }
        }

        public static double Pt100Resistance(double temperatureC)
        {
            if (temperatureC < -200 || temperatureC > 850) throw new ArgumentOutOfRangeException("temperatureC", "Pt100 范围为 -200…850°C");
            const double r0 = 100.0, a = 3.9083e-3, b = -5.775e-7, c = -4.183e-12;
            if (temperatureC >= 0) return r0 * (1 + a * temperatureC + b * temperatureC * temperatureC);
            return r0 * (1 + a * temperatureC + b * temperatureC * temperatureC + c * (temperatureC - 100) * temperatureC * temperatureC * temperatureC);
        }

        public static double Pt100Temperature(double resistanceOhm)
        {
            if (resistanceOhm < Pt100Resistance(-200) || resistanceOhm > Pt100Resistance(850))
                throw new ArgumentOutOfRangeException("resistanceOhm", "阻值超出 Pt100 IEC 60751 范围");
            double lo = -200, hi = 850;
            for (int i = 0; i < 80; i++)
            {
                var mid = (lo + hi) / 2.0;
                if (Pt100Resistance(mid) < resistanceOhm) lo = mid; else hi = mid;
            }
            return (lo + hi) / 2.0;
        }

        public static float DecodeModbusFloat(ushort register1, ushort register2, string byteOrder)
        {
            byte[] src = { (byte)(register1 >> 8), (byte)register1, (byte)(register2 >> 8), (byte)register2 };
            byte[] ordered;
            switch ((byteOrder ?? "ABCD").ToUpperInvariant())
            {
                case "BADC": ordered = new[] { src[1], src[0], src[3], src[2] }; break;
                case "CDAB": ordered = new[] { src[2], src[3], src[0], src[1] }; break;
                case "DCBA": ordered = new[] { src[3], src[2], src[1], src[0] }; break;
                default: ordered = src; break;
            }
            uint bits = ((uint)ordered[0] << 24) | ((uint)ordered[1] << 16) | ((uint)ordered[2] << 8) | ordered[3];
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }

        public static ushort ParseRegister(string text)
        {
            var s = (text ?? "").Trim();
            int value;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) throw new FormatException("寄存器十六进制格式无效");
            }
            else if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) throw new FormatException("寄存器数值无效");
            if (value < 0 || value > 65535) throw new ArgumentOutOfRangeException("text", "寄存器范围为 0…65535");
            return (ushort)value;
        }

        public static S7AddressResult ParseS7Address(string input)
        {
            var s = Regex.Replace((input ?? "").Trim().ToUpperInvariant(), @"\s+", "");
            var db = Regex.Match(s, @"^DB(?<db>\d+)\.DB(?<type>[XBWD])(?<byte>\d+)(?:\.(?<bit>\d+))?$");
            if (db.Success)
            {
                int dbn = int.Parse(db.Groups["db"].Value), by = int.Parse(db.Groups["byte"].Value);
                string type = db.Groups["type"].Value;
                int bit = db.Groups["bit"].Success ? int.Parse(db.Groups["bit"].Value) : -1;
                if (type == "X" && (bit < 0 || bit > 7)) return Bad("DBX 位地址必须是 0…7");
                if (type != "X" && bit >= 0) return Bad("字节/字/双字地址不能带位号");
                int size = type == "X" ? 1 : type == "B" ? 8 : type == "W" ? 16 : 32;
                return new S7AddressResult { Ok = true, Area = "DB", DbNumber = dbn, ByteOffset = by, BitOffset = bit, SizeBits = size, Canonical = "DB" + dbn + ".DB" + type + by + (bit >= 0 ? "." + bit : ""), Description = "数据块 DB" + dbn + "，字节偏移 " + by + (bit >= 0 ? "，位 " + bit : "") + "，宽度 " + size + " bit" };
            }

            var area = Regex.Match(s, @"^(?<area>[IQM])(?<type>[BWD]?)(?<byte>\d+)(?:\.(?<bit>\d+))?$");
            if (area.Success)
            {
                string a = area.Groups["area"].Value, type = area.Groups["type"].Value;
                int by = int.Parse(area.Groups["byte"].Value), bit = area.Groups["bit"].Success ? int.Parse(area.Groups["bit"].Value) : -1;
                if (type.Length == 0 && bit < 0) type = "B";
                if (bit >= 0 && type.Length > 0) return Bad("位地址请使用 I0.0 / Q0.0 / M0.0 格式");
                if (bit > 7) return Bad("位号必须是 0…7");
                int size = bit >= 0 ? 1 : type == "W" ? 16 : type == "D" ? 32 : 8;
                string label = a == "I" ? "输入" : a == "Q" ? "输出" : "标志位";
                return new S7AddressResult { Ok = true, Area = a, ByteOffset = by, BitOffset = bit, SizeBits = size, Canonical = a + (bit >= 0 ? "" : type) + by + (bit >= 0 ? "." + bit : ""), Description = label + "区，字节偏移 " + by + (bit >= 0 ? "，位 " + bit : "") + "，宽度 " + size + " bit" };
            }
            return Bad("无法识别地址。示例：DB10.DBX2.3、DB10.DBW4、I0.0、QW2、MD10");
        }

        private static S7AddressResult Bad(string error) { return new S7AddressResult { Error = error }; }
    }
}
