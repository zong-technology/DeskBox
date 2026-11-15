using System;
using System.Runtime.InteropServices;

namespace DeskBox.Native
{
    /// <summary>Win+F fallback when Explorer already owns RegisterHotKey. No key history is retained.</summary>
    public sealed class WinFShortcut : IDisposable
    {
        private IntPtr _hook;
        private readonly HookProc _callback;
        private readonly Action _trigger;
        private bool _consumingF;
        public WinFShortcut(Action trigger) { _trigger=trigger; _callback=OnKeyboard; }
        public bool Start() { _hook=SetWindowsHookEx(13,_callback,GetModuleHandle(null),0);return _hook!=IntPtr.Zero; }
        // 0: pass, 1: consume key, 2: consume and invoke once.
        public int ProcessGesture(uint key,bool down,bool winDown,bool otherModifiers)
        {
            if(key!=0x46)return 0;
            if(!down){if(!_consumingF)return 0;_consumingF=false;return 1;}
            if(_consumingF)return 1;
            if(!winDown||otherModifiers)return 0;
            _consumingF=true;return 2;
        }
        private IntPtr OnKeyboard(int code,IntPtr message,IntPtr data)
        {
            if(code>=0)
            {
                var key=(KeyboardData)Marshal.PtrToStructure(data,typeof(KeyboardData));
                if((key.flags&0x10)==0)
                {
                    int msg=message.ToInt32();bool down=msg==0x100||msg==0x104,up=msg==0x101||msg==0x105;
                    if(down||up)
                    {
                        int action=ProcessGesture(key.vkCode,down,Pressed(0x5B)||Pressed(0x5C),Pressed(0x10)||Pressed(0x11)||Pressed(0x12));
                        if(action==2)
                        {
                            // Mark the Windows key as used so releasing it does not open Start over DeskBox.
                            var input=new[]{new Input{type=1,payload=new InputPayload{keyboard=new KeyboardInput{vk=0x11}}},new Input{type=1,payload=new InputPayload{keyboard=new KeyboardInput{vk=0x11,flags=2}}}};
                            SendInput((uint)input.Length,input,Marshal.SizeOf(typeof(Input)));
                            try{_trigger();}catch{}
                        }
                        if(action!=0)return new IntPtr(1);
                    }
                }
            }
            return CallNextHookEx(_hook,code,message,data);
        }
        private static bool Pressed(int key){return (GetAsyncKeyState(key)&0x8000)!=0;}
        public void Dispose(){if(_hook!=IntPtr.Zero){UnhookWindowsHookEx(_hook);_hook=IntPtr.Zero;}}
        private delegate IntPtr HookProc(int code,IntPtr message,IntPtr data);
        [StructLayout(LayoutKind.Sequential)]private struct KeyboardData{public uint vkCode,scanCode,flags,time;public UIntPtr extra;}
        [StructLayout(LayoutKind.Sequential)]private struct KeyboardInput{public ushort vk,scan;public uint flags,time;public UIntPtr extra;}
        [StructLayout(LayoutKind.Sequential)]private struct MouseInput{public int x,y;public uint mouseData,flags,time;public UIntPtr extra;}
        [StructLayout(LayoutKind.Explicit)]private struct InputPayload{[FieldOffset(0)]public KeyboardInput keyboard;[FieldOffset(0)]public MouseInput mouse;}
        [StructLayout(LayoutKind.Sequential)]private struct Input{public uint type;public InputPayload payload;}
        [DllImport("user32.dll",SetLastError=true)]private static extern IntPtr SetWindowsHookEx(int id,HookProc callback,IntPtr module,uint thread);
        [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
        [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll",SetLastError=true)]private static extern uint SendInput(uint count,Input[] input,int size);
        [DllImport("kernel32.dll",CharSet=CharSet.Auto)]private static extern IntPtr GetModuleHandle(string name);
    }
}
