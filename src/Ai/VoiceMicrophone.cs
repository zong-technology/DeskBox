using System;
using System.Runtime.InteropServices;

namespace DeskBox.Ai
{
    // P1 local input, 20 ms mono PCM16 blocks. Only the owning thread polls and closes it.
    internal sealed class VoiceMicrophone : IVoiceInput
    {
        public const int SampleRate = 24000;
        public const int SamplesPerBlock = SampleRate / 50;
        private const uint Done = 1;
        private static readonly uint NativeSize = (uint)Marshal.SizeOf(typeof(Header));
        private readonly IntPtr[] _headers = new IntPtr[4];
        private readonly IntPtr[] _buffers = new IntPtr[4];
        private readonly bool[] _prepared = new bool[4];
        private IntPtr _device;
        private int _next;
        private readonly int _blockSamples;

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        internal struct Format
        {
            public ushort Tag, Channels;
            public uint Rate, BytesPerSecond;
            public ushort Alignment, Bits, Extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Header
        {
            public IntPtr Data;
            public uint Length, Recorded;
            public UIntPtr User;
            public uint Flags, Loops;
            public IntPtr Next;
            public UIntPtr Reserved;
        }

        [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();
        [DllImport("winmm.dll")] private static extern uint waveInOpen(out IntPtr handle, uint device, ref Format format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] private static extern uint waveInPrepareHeader(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveInUnprepareHeader(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveInAddBuffer(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveInStart(IntPtr handle);
        [DllImport("winmm.dll")] private static extern uint waveInReset(IntPtr handle);
        [DllImport("winmm.dll")] private static extern uint waveInClose(IntPtr handle);

        public static uint DeviceCount { get { return waveInGetNumDevs(); } }

        public VoiceMicrophone(int rate = SampleRate)
        {
            if (rate != 16000 && rate != SampleRate) throw new ArgumentOutOfRangeException("rate");
            _blockSamples = rate / 50;
            var format = new Format { Tag = 1, Channels = 1, Rate = (uint)rate, BytesPerSecond = (uint)rate * 2, Alignment = 2, Bits = 16 };
            Check(waveInOpen(out _device, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0), "打开麦克风");
            try
            {
                for (int i = 0; i < _headers.Length; i++)
                {
                    _buffers[i] = Marshal.AllocHGlobal(_blockSamples * 2);
                    _headers[i] = Marshal.AllocHGlobal((int)NativeSize);
                    Marshal.StructureToPtr(new Header { Data = _buffers[i], Length = (uint)_blockSamples * 2 }, _headers[i], false);
                    Check(waveInPrepareHeader(_device, _headers[i], NativeSize), "准备录音缓冲");
                    _prepared[i] = true;
                    Check(waveInAddBuffer(_device, _headers[i], NativeSize), "提交录音缓冲");
                }
                Check(waveInStart(_device), "启动采集");
            }
            catch { Dispose(); throw; }
        }

        public int Read(short[] samples)
        {
            if (_device == IntPtr.Zero) throw new ObjectDisposedException("VoiceMicrophone");
            if (samples.Length < _blockSamples) throw new ArgumentException("PCM block too small.");
            var header = (Header)Marshal.PtrToStructure(_headers[_next], typeof(Header));
            if ((header.Flags & Done) == 0) return 0;
            var count = (int)Math.Min(header.Recorded / 2, (uint)_blockSamples);
            Marshal.Copy(header.Data, samples, 0, count);
            Check(waveInAddBuffer(_device, _headers[_next], NativeSize), "重新提交录音缓冲");
            _next = (_next + 1) % _headers.Length;
            return count;
        }

        public static double Rms(short[] samples, int count)
        {
            if (count == 0) return 0;
            double total = 0;
            for (int i = 0; i < count; i++)
            {
                double sample = samples[i] / 32768.0;
                total += sample * sample;
            }
            return Math.Sqrt(total / count);
        }

        public void Dispose()
        {
            if (_device == IntPtr.Zero) return;
            waveInReset(_device);
            for (int i = 0; i < _headers.Length; i++)
            {
                // A driver that still owns a buffer must not see freed memory.
                if (_prepared[i] && waveInUnprepareHeader(_device, _headers[i], NativeSize) != 0) continue;
                if (_headers[i] != IntPtr.Zero) Marshal.FreeHGlobal(_headers[i]);
                if (_buffers[i] != IntPtr.Zero) Marshal.FreeHGlobal(_buffers[i]);
            }
            waveInClose(_device);
            _device = IntPtr.Zero;
        }

        private static void Check(uint result, string operation)
        {
            if (result != 0) throw new InvalidOperationException(operation + "失败（WinMM " + result + "）。");
        }
    }
}
