// This software is based on "hooookk.dpr" from the software "ccchattttter".
//
// This is free and unencumbered software released into the public domain.
//
// Anyone is free to copy, modify, publish, use, compile, sell, or
// distribute this software, either in source code form or as a compiled
// binary, for any purpose, commercial or non-commercial, and by any
// means.
//
// In jurisdictions that recognize copyright laws, the author or authors
// of this software dedicate any and all copyright interest in the
// software to the public domain. We make this dedication for the benefit
// of the public at large and to the detriment of our heirs and
// successors. We intend this dedication to be an overt act of
// relinquishment in perpetuity of all present and future rights to this
// software under copyright law.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
// IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR
// OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
// ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
//
// For more information, please refer to <https://unlicense.org>

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KbdBounceGuard;

static partial class KbdBounceGuardClass
{

    // 
    // 

    [UnmanagedCallersOnly(EntryPoint = "StartHook", CallConvs = [typeof(CallConvStdcall)])]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe bool StartHook(HookSettings* pSettings)
    {
        if (pSettings is null || StateManager.HookHandle != 0)
            return false;

        try
        {
            StateManager.Settings = *pSettings;
            StateManager.PrevDownKey = 0;
            Array.Clear(StateManager.KeyStates);

            var hookDelegate = new LowLevelKeyboardProc(HookCallback);
            StateManager.HookDelegateHandle = GCHandle.Alloc(hookDelegate);
            StateManager.HookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, hookDelegate, Process.GetCurrentProcess().MainModule!.BaseAddress, 0u);
            if (StateManager.HookHandle == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            return true;
        }
        catch
        {
            if (StateManager.HookDelegateHandle.IsAllocated)
                StateManager.HookDelegateHandle.Free();
        }

        return false;
    }

    [UnmanagedCallersOnly(EntryPoint = "EndHook", CallConvs = [typeof(CallConvStdcall)])]
    public static void EndHook()
    {
        if (StateManager.HookHandle != 0)
        {
            UnhookWindowsHookEx(StateManager.HookHandle);
            StateManager.HookHandle = 0;
            if (StateManager.HookDelegateHandle.IsAllocated)
                StateManager.HookDelegateHandle.Free();
        }
    }

    static nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (StateManager.HookHandle != 0 && nCode >= 0)
        {
            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (kbd.vkCode < StateManager.KeyStates.Length)
            {
                var time1 = Environment.TickCount64;//kbd.time;
                ref var keyState = ref StateManager.KeyStates[(int)kbd.vkCode];

                if ((kbd.flags & LLKHF_UP) != 0)
                {
                    // key up
                    keyState.PrevUpTime = time1;
                    var time2 = keyState.PrevDownTime != 0 ? time1 - keyState.PrevDownTime : StateManager.Settings.UpDownThreshold;
                    if (StateManager.Settings.KeyUpChatter && time2 < StateManager.Settings.UpDownThreshold && keyState.NextUpFlag != 1 && !keyState.Repeat)
                    {
                        keyState.PrevChatter = true;
                        keyState.NextDownFlag = 2;
                        return 1;// cancel event
                    }
                    keyState.NextUpFlag = 0;
                }
                else
                {
                    // key down
                    var time2 = keyState.PrevDownTime != 0 ? time1 - keyState.PrevDownTime : 0;
                    var chatter = kbd.vkCode == StateManager.PrevDownKey && time2 < StateManager.Settings.ChatterThreshold && !(StateManager.Settings.IgnoreRepeat && keyState.Repeat);
                    keyState.NextUpFlag = 0;
                    if (!chatter && StateManager.Settings.KeyUpChatter && keyState.PrevUpTime != 0 && keyState.PrevDownTime < keyState.PrevUpTime && time1 - keyState.PrevUpTime < StateManager.Settings.UpDownThreshold)
                        (chatter, keyState.NextUpFlag) = (true, 2);
                    if (!chatter && keyState.NextDownFlag == 2 && time1 - keyState.PrevUpTime < StateManager.Settings.UpDownThreshold)
                        chatter = true;
                    keyState.Repeat = keyState.PrevUpTime < keyState.PrevDownTime;
                    if (chatter && StateManager.Settings.IgnoreRepeat && StateManager.Settings.RepeatThreshold <= time2 && keyState.Repeat)
                        chatter = false;

                    keyState.PrevDownTime = time1;
                    keyState.PrevChatter = chatter;
                    StateManager.PrevDownKey = kbd.vkCode;

                    if (!ValidateImeCtrlBackspace(kbd.vkCode, time1))
                        keyState.NextDownFlag = 0;
                    else
                    {
                        chatter = false;
                        keyState.NextDownFlag = keyState.NextUpFlag = 1;
                    }

                    if (chatter)
                        return 1;// cancel event
                }
            }
        }

        return CallNextHookEx(StateManager.HookHandle, nCode, wParam, lParam);
    }

    static bool ValidateImeCtrlBackspace(uint code, long time)
    {
        if (!StateManager.Settings.AllowImeCtrlBackspace)
            return false;
        if (code != VK_BACK)
            return false;

        ref var keyBackspace = ref StateManager.KeyStates[VK_BACK];
        if (keyBackspace.NextDownFlag == 1 && time - keyBackspace.PrevDownTime < StateManager.Settings.ChatterThreshold)
            return true;

        ref var keyLControl = ref StateManager.KeyStates[VK_LCONTROL];
        if (keyLControl.PrevDownTime != 0 && keyLControl.PrevUpTime <= keyLControl.PrevDownTime)
            return true;

        ref var keyRControl = ref StateManager.KeyStates[VK_RCONTROL];
        if (keyRControl.PrevDownTime != 0 && keyRControl.PrevUpTime <= keyRControl.PrevDownTime)
            return true;

        return false;
    }

    // 
    // 

    const int WH_KEYBOARD_LL = 13;
    const uint LLKHF_UP = 0x80u;
    const int VK_BACK = 0x08;
    // const int VK_CONTROL = 0x11;
    const int VK_LCONTROL = 0xA2;
    const int VK_RCONTROL = 0xA3;

    delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static partial nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HookSettings
    {
        public int ChatterThreshold;
        public int RepeatThreshold;
        public int UpDownThreshold;
        [MarshalAs(UnmanagedType.Bool)] public bool IgnoreRepeat;
        [MarshalAs(UnmanagedType.Bool)] public bool KeyUpChatter;
        [MarshalAs(UnmanagedType.Bool)] public bool AllowImeCtrlBackspace;
    }

    struct KeyState
    {
        public long PrevDownTime;
        public long PrevUpTime;
        public int NextUpFlag;
        public int NextDownFlag;
        public bool PrevChatter;
        public bool Repeat;
    }

    static class StateManager
    {
        public static GCHandle HookDelegateHandle;
        public static nint HookHandle = 0;
        public static HookSettings Settings;
        public static uint PrevDownKey;
        public static readonly KeyState[] KeyStates = new KeyState[256];
    }
}
