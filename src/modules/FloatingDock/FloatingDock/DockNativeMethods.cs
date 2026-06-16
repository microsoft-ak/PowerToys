// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.Runtime.InteropServices;

using Microsoft.Win32;

namespace Microsoft.PowerToys.FloatingDock;

/// <summary>
/// Win32/DWM interop used to give the WinForms dock and its menus/dialogs a modern,
/// theme-aware appearance: rounded corners and immersive dark/light title styling for
/// the dock window (which uses a solid themed fill), plus an acrylic blur-behind material
/// for the context menus and the Add/Rename dialog (like the Command Palette window).
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

    // SHGetFileInfo flags for extracting a shell icon handle.
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr Handle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref ShFileInfo fileInfo, uint sizeFileInfo, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

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

    /// <summary>
    /// Returns the shell icon for <paramref name="path"/> as a bitmap, or <c>null</c> when
    /// it cannot be resolved. Used to render real folder icons (which
    /// <see cref="System.Drawing.Icon.ExtractAssociatedIcon"/> does not provide).
    /// </summary>
    public static Bitmap? TryGetShellIcon(string path)
    {
        var info = default(ShFileInfo);
        var result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiLargeIcon);
        if (result == IntPtr.Zero || info.Handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            using var icon = Icon.FromHandle(info.Handle);
            return icon.ToBitmap();
        }
        catch
        {
            return null;
        }
        finally
        {
            // Icon.FromHandle does not own the handle, so release the one SHGetFileInfo created.
            DestroyIcon(info.Handle);
        }
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
