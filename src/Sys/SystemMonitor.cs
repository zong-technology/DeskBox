using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using DeskBox.Core;

namespace DeskBox.Sys
{
    /// <summary>
    /// 系统监控：CPU（GetSystemTimes 差分）/ 内存（GlobalMemoryStatusEx）/ 磁盘（DriveInfo）/
    /// 网络（IPv4Statistics 差分）。全部数字来自系统接口，失败只写 Error，不让面板崩。
    /// 注意：Updated 在后台线程（线程池计时器）触发，UI 订阅方请自行 BeginInvoke。
    /// 第一次采样 CPU / 网络速率必然是 0（没有历史基线），这是预期行为。
    /// </summary>
    public class SystemMonitor : IDisposable
    {
        public const int DefaultIntervalMs = 2000;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeFileTime
        {
            public uint Low;
            public uint High;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMemoryStatus
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out NativeFileTime idleTime, out NativeFileTime kernelTime, out NativeFileTime userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref NativeMemoryStatus buffer);

        private readonly int _intervalMs;
        private readonly object _lock = new object();        // 保护 _last / _timer
        private readonly object _sampleLock = new object();  // 串行化采样，避免差分基线被并发打乱

        private System.Threading.Timer _timer;
        private SystemSample _last;

        private bool _hasCpuPrev;
        private ulong _prevIdle;
        private ulong _prevKernel;
        private ulong _prevUser;

        private bool _hasNetPrev;
        private long _prevRx;
        private long _prevTx;
        private long _prevNetStamp;

        /// <summary>每次采样完成后触发（后台线程）。</summary>
        public event Action<SystemSample> Updated;

        public SystemMonitor() : this(DefaultIntervalMs) { }

        public SystemMonitor(int intervalMs)
        {
            _intervalMs = intervalMs > 0 ? intervalMs : DefaultIntervalMs;
            _last = new SystemSample();
            _last.Ts = NowMs();
        }

        public int IntervalMs { get { return _intervalMs; } }

        /// <summary>最近一次采样（首次采样前是空样本）。</summary>
        public SystemSample Last
        {
            get { lock (_lock) { return _last; } }
        }

        public bool Running
        {
            get { lock (_lock) { return _timer != null; } }
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_timer != null) return;
                try
                {
                    // dueTime=0：启动后立刻采一次（CPU/网络为 0），随后按间隔周期采样
                    _timer = new System.Threading.Timer(OnTick, null, 0, _intervalMs);
                }
                catch (Exception ex)
                {
                    DeskBox.Core.AppLog.Write("error", "monitor", "启动采样计时器失败: " + ex.Message);
                }
            }
        }

        public void Stop()
        {
            System.Threading.Timer timer;
            lock (_lock)
            {
                timer = _timer;
                _timer = null;
            }
            if (timer == null) return;
            try { timer.Dispose(); }
            catch (Exception ex) { DeskBox.Core.AppLog.Write("warn", "monitor", "停止采样计时器失败: " + ex.Message); }
        }

        public void Dispose() { Stop(); }

        private void OnTick(object state)
        {
            try { SampleNow(); }
            catch (Exception ex) { DeskBox.Core.AppLog.Write("error", "monitor", "采样回调异常: " + ex.Message); }
        }

        /// <summary>立即同步采样一次并触发 Updated（可在任意线程调用）。</summary>
        public void SampleNow()
        {
            SystemSample sample;
            lock (_sampleLock)
            {
                var errors = new List<string>();
                try { sample = BuildSample(errors); }
                catch (Exception ex)
                {
                    sample = new SystemSample();
                    sample.Ts = NowMs();
                    errors.Add("采样异常: " + ex.Message);
                }
                sample.Error = string.Join("；", errors.ToArray());
                lock (_lock) { _last = sample; }
            }

            var handler = Updated;
            if (handler != null)
            {
                try { handler(sample); }
                catch (Exception ex)
                {
                    DeskBox.Core.AppLog.Write("warn", "monitor", "Updated 订阅者异常: " + ex.Message);
                }
            }
        }

        // ------------------------------------------------------------------
        // 采样
        // ------------------------------------------------------------------

        private SystemSample BuildSample(List<string> errors)
        {
            var sample = new SystemSample();
            sample.Ts = NowMs();

            SampleCpu(sample, errors);
            SampleMemory(sample, errors);
            SampleDisks(sample, errors);
            SampleNetwork(sample, errors);

            return sample;
        }

        private void SampleCpu(SystemSample sample, List<string> errors)
        {
            try
            {
                NativeFileTime idle, kernel, user;
                if (!GetSystemTimes(out idle, out kernel, out user))
                {
                    errors.Add("GetSystemTimes 失败: " + Marshal.GetLastWin32Error());
                    return;
                }

                ulong i = ToU64(idle);
                ulong k = ToU64(kernel);
                ulong u = ToU64(user);

                // kernel 已包含 idle；CPU 忙 = (kernel + user - idle) / (kernel + user)
                if (_hasCpuPrev && i >= _prevIdle && k >= _prevKernel && u >= _prevUser)
                {
                    ulong dIdle = i - _prevIdle;
                    ulong dKernel = k - _prevKernel;
                    ulong dUser = u - _prevUser;
                    ulong dTotal = dKernel + dUser;
                    if (dTotal > 0)
                    {
                        double busy = (double)dTotal - (double)dIdle;
                        double pct = busy * 100.0 / dTotal;
                        if (pct < 0) pct = 0;
                        if (pct > 100) pct = 100;
                        sample.CpuPercent = Math.Round(pct, 1);
                    }
                }

                _prevIdle = i;
                _prevKernel = k;
                _prevUser = u;
                _hasCpuPrev = true;
            }
            catch (Exception ex)
            {
                errors.Add("CPU: " + ex.Message);
            }
        }

        private void SampleMemory(SystemSample sample, List<string> errors)
        {
            try
            {
                var ms = new NativeMemoryStatus();
                ms.Length = (uint)Marshal.SizeOf(typeof(NativeMemoryStatus));
                if (!GlobalMemoryStatusEx(ref ms))
                {
                    errors.Add("GlobalMemoryStatusEx 失败: " + Marshal.GetLastWin32Error());
                    return;
                }

                long total = (long)ms.TotalPhys;
                long avail = (long)ms.AvailPhys;
                if (total < 0) total = 0;
                if (avail < 0) avail = 0;
                if (avail > total) avail = total;

                sample.MemTotal = total;
                sample.MemUsed = total - avail;
                sample.MemUsedPercent = total > 0 ? Math.Round((total - avail) * 100.0 / total, 1) : 0;
            }
            catch (Exception ex)
            {
                errors.Add("内存: " + ex.Message);
            }
        }

        private void SampleDisks(SystemSample sample, List<string> errors)
        {
            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                bool reportedError = false;
                for (int i = 0; i < drives.Length; i++)
                {
                    try
                    {
                        DriveInfo d = drives[i];
                        if (!d.IsReady) continue;
                        if (d.DriveType != DriveType.Fixed) continue;

                        var vol = new DiskVolume();
                        vol.Name = d.Name;
                        try { vol.Label = d.VolumeLabel ?? ""; }
                        catch { vol.Label = ""; }
                        try
                        {
                            vol.Total = d.TotalSize;
                            vol.Free = d.AvailableFreeSpace;
                        }
                        catch
                        {
                            vol.Total = 0;
                            vol.Free = 0;
                        }
                        sample.Disks.Add(vol);
                    }
                    catch (Exception ex)
                    {
                        if (!reportedError)
                        {
                            errors.Add("磁盘: " + ex.Message);
                            reportedError = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add("磁盘: " + ex.Message);
            }
        }

        private void SampleNetwork(SystemSample sample, List<string> errors)
        {
            try
            {
                long rx = 0, tx = 0;
                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < nics.Length; i++)
                {
                    try
                    {
                        NetworkInterface nic = nics[i];
                        if (nic.OperationalStatus != OperationalStatus.Up) continue;
                        if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                        if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;   // VPN 虚拟接口不重复计数

                        IPv4InterfaceStatistics stat = nic.GetIPv4Statistics();
                        if (stat.BytesReceived > 0) rx += stat.BytesReceived;
                        if (stat.BytesSent > 0) tx += stat.BytesSent;
                    }
                    catch { /* 单个网卡不可读：忽略，不影响总量 */ }
                }

                long stamp = Stopwatch.GetTimestamp();
                if (_hasNetPrev)
                {
                    double seconds = (stamp - _prevNetStamp) / (double)Stopwatch.Frequency;
                    if (seconds > 0.05)
                    {
                        long dRx = rx - _prevRx;
                        long dTx = tx - _prevTx;
                        if (dRx >= 0) sample.NetRxBps = Math.Round(dRx / seconds, 1);
                        if (dTx >= 0) sample.NetTxBps = Math.Round(dTx / seconds, 1);
                    }
                }

                _prevRx = rx;
                _prevTx = tx;
                _prevNetStamp = stamp;
                _hasNetPrev = true;
            }
            catch (Exception ex)
            {
                errors.Add("网络: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // 辅助
        // ------------------------------------------------------------------

        private static ulong ToU64(NativeFileTime ft)
        {
            return ((ulong)ft.High << 32) | ft.Low;
        }

        private static long NowMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
