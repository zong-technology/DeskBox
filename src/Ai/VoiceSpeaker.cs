using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DeskBox.Ai
{
    internal sealed class VoiceSpeaker : IDisposable
    {
        private sealed class Block
        {
            public IntPtr Data, Header;
            public byte[] Pcm;
            public bool Prepared;
            public long Start;
        }

        private static readonly uint HeaderSize = (uint)Marshal.SizeOf(typeof(VoiceMicrophone.Header));
        private readonly Queue<Block> _blocks = new Queue<Block>();
        private readonly int _rate;
        private long _written;
        private IntPtr _device;

        [StructLayout(LayoutKind.Sequential)]
        private struct Position { public uint Type, Value, Reserved; }

        [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr device, uint id, ref VoiceMicrophone.Format format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutGetPosition(IntPtr device, ref Position pos, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr device);
        [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr device);

        public VoiceSpeaker(int rate)
        {
            _rate = rate;
            var format = new VoiceMicrophone.Format { Tag = 1, Channels = 1, Rate = (uint)rate, BytesPerSecond = (uint)rate * 2, Alignment = 2, Bits = 16 };
            Check(waveOutOpen(out _device, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0));
        }

        public void Play(byte[] pcm)
        {
            Reap();
            if (_blocks.Count != 0 && _written - _blocks.Peek().Start + pcm.Length / 2 > 15L * _rate)
                throw new InvalidOperationException("扬声器缓冲超过 15 秒，请重新连接。");
            var b = new Block { Pcm = pcm, Start = _written };
            try
            {
                b.Data = Marshal.AllocHGlobal(pcm.Length);
                b.Header = Marshal.AllocHGlobal((int)HeaderSize);
                Marshal.Copy(pcm, 0, b.Data, pcm.Length);
                Marshal.StructureToPtr(new VoiceMicrophone.Header { Data = b.Data, Length = (uint)pcm.Length }, b.Header, false);
                Check(waveOutPrepareHeader(_device, b.Header, HeaderSize));
                b.Prepared = true;
                Check(waveOutWrite(_device, b.Header, HeaderSize));
            }
            catch { Free(b); throw; }
            _written += pcm.Length / 2;
            _blocks.Enqueue(b);
        }

        public double? PlaybackLevel()
        {
            Reap();
            if (_blocks.Count == 0) return null;
            var position = new Position { Type = 2 }; // TIME_SAMPLES
            if (waveOutGetPosition(_device, ref position, (uint)Marshal.SizeOf(typeof(Position))) != 0 || position.Type != 2)
                return 0;
            foreach (var block in _blocks)
            {
                long offset = position.Value - block.Start;
                int length = block.Pcm.Length / 2;
                if (offset < 0 || offset >= length) continue;
                int count = Math.Min(length - (int)offset, _rate / 50);
                double sum = 0;
                for (int i = 0; i < count; i++)
                {
                    int j = ((int)offset + i) * 2;
                    double x = (short)(block.Pcm[j] | block.Pcm[j + 1] << 8) / 32768.0;
                    sum += x * x;
                }
                return Math.Sqrt(sum / count);
            }
            return 0;
        }

        private void Reap()
        {
            while (_blocks.Count != 0)
            {
                var b = _blocks.Peek();
                var header = (VoiceMicrophone.Header)Marshal.PtrToStructure(b.Header, typeof(VoiceMicrophone.Header));
                if ((header.Flags & 1) == 0) return;
                Free(_blocks.Dequeue());
            }
        }

        public void Flush()
        {
            Check(waveOutReset(_device));
            while (_blocks.Count != 0) Free(_blocks.Dequeue());
            _written = 0;
        }

        public void Dispose()
        {
            if (_device == IntPtr.Zero) return;
            if (waveOutReset(_device) == 0)
                while (_blocks.Count != 0) Free(_blocks.Dequeue());
            waveOutClose(_device);
            _device = IntPtr.Zero;
        }

        private void Free(Block b)
        {
            if (b.Prepared && waveOutUnprepareHeader(_device, b.Header, HeaderSize) != 0) return;
            if (b.Header != IntPtr.Zero) Marshal.FreeHGlobal(b.Header);
            if (b.Data != IntPtr.Zero) Marshal.FreeHGlobal(b.Data);
        }

        private static void Check(uint result)
        {
            if (result != 0) throw new InvalidOperationException("扬声器操作失败（WinMM " + result + "）。");
        }
    }
}
