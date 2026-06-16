// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.Runtime.InteropServices;

using Microsoft.Win32;

namespace FloatingDock;

/// <summary>
/// Win32/DWM interop used to give the WinForms dock a modern, theme-aware,
/// glassmorphic appearance (rounded corners, immersive dark mode title styling,
/// and acrylic blur-behind material like the Command Palette window).
/// </summary>
internal static class DockNativeMethods
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const int DwmwcpRoundSmall = 3;

    // SetWindowCompositionAttribute / accent policy (acrylic blur-behind).
    private const int WcaAccentPolicy = 19;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int AccentEnableBlurBehind = 3;

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    /// <summary>
    /// Reads the Windows "Apps use light theme" preference from the registry.
    /// Defaults to dark when the value is missing.
    /// </summary>
    public static bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            if (key?.GetValue("AppsUseLightTheme") is int value)
            {
                return value != 0;
            }
        }
        catch
        {
        }

        return false;
    }

    /// <summary>Applies the dark/light immersive mode flag (affects DWM-drawn chrome).</summary>
    public static void SetImmersiveDarkMode(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var value = dark ? 1 : 0;
        try
        {
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
        }
        catch
        {
        }
    }

    /// <summary>Requests rounded window corners on Windows 11.</summary>
    public static void SetRoundedCorners(IntPtr hwnd, bool small = false)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var preference = small ? DwmwcpRoundSmall : DwmwcpRound;
        try
        {
            _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
        catch
        {
        }
    }

    /// <summary>
    /// Enables an acrylic blur-behind material tinted with <paramref name="tint"/>.
    /// Falls back to a plain blur on builds without acrylic support, and is a no-op
    /// when the call is unavailable.
    /// </summary>
    public static void EnableAcrylic(IntPtr hwnd, Color tint)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // DWM expects the gradient color as 0xAABBGGRR.
        var gradient = (tint.A << 24) | (tint.B << 16) | (tint.G << 8) | tint.R;
        var accentState = SupportsAcrylic() ? AccentEnableAcrylicBlurBehind : AccentEnableBlurBehind;

        var accent = new AccentPolicy
        {
            AccentState = accentState,
            AccentFlags = 2,
            GradientColor = gradient,
        };

        var size = Marshal.SizeOf(accent);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = ptr,
                SizeOfData = size,
            };

            _ = SetWindowCompositionAttribute(hwnd, ref data);
        }
        catch
        {
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static bool SupportsAcrylic()
    {
        // Acrylic blur-behind is available from Windows 10 1803 (build 17134).
        var version = Environment.OSVersion.Version;
        return version.Major > 10 || (version.Major == 10 && version.Build >= 17134);
    }
}
