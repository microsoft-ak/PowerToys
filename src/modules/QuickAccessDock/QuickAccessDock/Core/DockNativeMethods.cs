// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

using Microsoft.Win32;

namespace QuickAccessDock;

/// <summary>
/// Win32/DWM interop used to give the WinForms dock a modern, theme-aware
/// appearance (rounded corners, immersive dark mode title styling, and an
/// acrylic blur-behind material for context menus).
/// </summary>
internal static class DockNativeMethods
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwcpRound = 2;
    private const int DwmwcpRoundSmall = 3;

    // DWM system backdrop reset value. The dock uses tint-controlled accent acrylic for
    // its body, so any system backdrop material must be cleared before that is applied.
    private const int DwmsbtNone = 1;

    // SetWindowCompositionAttribute / accent policy (acrylic blur-behind).
    private const int WcaAccentPolicy = 19;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int AccentEnableBlurBehind = 3;
    private const int AccentDisabled = 0;
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeReadonly = 0x00000001;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";

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

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int LeftWidth;
        public int RightWidth;
        public int TopHeight;
        public int BottomHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct ShFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref ShFileInfo fileInfo, uint fileInfoSize, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

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
            // Registry access can fail in restricted environments; fall back to dark theme.
        }

        return false;
    }

    /// <summary>
    /// Reads the user's Windows accent color (Settings ► Personalization ► Colors) so the
    /// app can honor the system accent by default, as Fluent design expects, instead of a
    /// hardcoded brand color. Returns null when it can't be read (older builds or restricted
    /// environments), letting the caller fall back to its own default accent.
    /// </summary>
    public static Color? GetSystemAccentColor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DwmKey);

            // DWM stores the accent as a DWORD in 0xAABBGGRR (ABGR) order; the low byte is
            // red. Force full opacity — the stored alpha is not meaningful for a solid fill.
            if (key?.GetValue("AccentColor") is int abgr)
            {
                return Color.FromArgb(255, abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
            }
        }
        catch
        {
            // Registry access can fail in restricted environments; fall back to no override.
        }

        return null;
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
            // DWM attribute is unavailable on older Windows builds; silently skip.
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
            // Window corner preference is a Windows 11 feature; silently skip on Windows 10.
        }
    }

    /// <summary>
    /// Explicitly forces the window titlebar's text color (and optionally its caption
    /// background color), bypassing the automatic light/dark scheme that
    /// <see cref="SetImmersiveDarkMode"/> picks. Used where the automatic scheme isn't
    /// trustworthy enough to guarantee readable text regardless of how the OS would
    /// otherwise render it.
    /// </summary>
    public static void SetCaptionColors(IntPtr hwnd, Color? captionColor, Color? textColor)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (captionColor is Color caption)
            {
                var value = ToColorRef(caption);
                _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref value, sizeof(int));
            }

            if (textColor is Color text)
            {
                var value = ToColorRef(text);
                _ = DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref value, sizeof(int));
            }
        }
        catch
        {
            // Caption/text color attributes are Windows 11-only; silently skip elsewhere.
        }
    }

    // DWM caption/text color attributes take a COLORREF (0x00BBGGRR), not an ARGB value.
    private static int ToColorRef(Color color) => (color.B << 16) | (color.G << 8) | color.R;

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
        var gradient = ToAccentGradient(tint);
        var accentState = SupportsAcrylic() ? AccentEnableAcrylicBlurBehind : AccentEnableBlurBehind;

        _ = SetAccentPolicy(hwnd, accentState, gradient);
    }

    /// <summary>
    /// Turns the window into see-through frosted glass: an acrylic blur-behind material
    /// tinted by <paramref name="tint"/> (whose alpha controls how strongly the frost veils
    /// the blurred background — a low alpha keeps the dock clearly transparent rather than a
    /// flat grey/white slab). Uses the accent blur-behind API so the tint is fully
    /// controllable, unlike the system backdrop whose tint is fixed/opaque-grey. The caller
    /// must paint the client area with true transparent (alpha-0) pixels for the material to
    /// show through. Returns false on builds without acrylic support (pre-Win10 1803).
    /// </summary>
    public static bool TryEnableTintedAcrylic(IntPtr hwnd, bool dark, Color tint)
    {
        if (hwnd == IntPtr.Zero || !SupportsAcrylic())
        {
            return false;
        }

        try
        {
            SetImmersiveDarkMode(hwnd, dark);

            // Clear any system backdrop first so it can't override the accent material with
            // the OS' fixed grey/white acrylic.
            ResetSystemBackdrop(hwnd);

            return SetAccentPolicy(hwnd, AccentEnableAcrylicBlurBehind, ToAccentGradient(tint));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Removes the acrylic blur-behind material, returning the window to an opaque surface.
    /// Safe to call when no material is active.
    /// </summary>
    public static void DisableTintedAcrylic(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            ResetSystemBackdrop(hwnd);
            _ = SetAccentPolicy(hwnd, AccentDisabled, 0);
        }
        catch
        {
            // Resetting the backdrop is best-effort; ignore failures on older builds.
        }
    }

    private static int ToAccentGradient(Color tint) => (tint.A << 24) | (tint.B << 16) | (tint.G << 8) | tint.R;

    private static void ResetSystemBackdrop(IntPtr hwnd)
    {
        if (!SupportsSystemBackdrop())
        {
            return;
        }

        var backdrop = DwmsbtNone;
        _ = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));

        var noSheet = new Margins();
        _ = DwmExtendFrameIntoClientArea(hwnd, ref noSheet);
    }

    private static bool SetAccentPolicy(IntPtr hwnd, int accentState, int gradient)
    {
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

            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch
        {
            // Acrylic/blur-behind is an optional visual enhancement; silently skip if unavailable.
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public static Icon? GetShellIcon(string path, bool largeIcon)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var flags = ShgfiIcon | (largeIcon ? ShgfiLargeIcon : ShgfiSmallIcon);
        uint attributes;

        if (Directory.Exists(path))
        {
            attributes = FileAttributeDirectory | FileAttributeReadonly;
        }
        else if (File.Exists(path))
        {
            attributes = FileAttributeNormal;
        }
        else
        {
            attributes = FileAttributeNormal;
            flags |= ShgfiUseFileAttributes;
        }

        var info = new ShFileInfo();
        try
        {
            var result = SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags);
            if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero)
            {
                return null;
            }

            return (Icon)Icon.FromHandle(info.IconHandle).Clone();
        }
        catch
        {
            return null;
        }
        finally
        {
            if (info.IconHandle != IntPtr.Zero)
            {
                _ = DestroyIcon(info.IconHandle);
            }
        }
    }

    private static bool SupportsAcrylic()
    {
        // Acrylic blur-behind is available from Windows 10 1803 (build 17134).
        var version = Environment.OSVersion.Version;
        return version.Major > 10 || (version.Major == 10 && version.Build >= 17134);
    }

    /// <summary>
    /// Whether the acrylic blur-behind material is available (Windows 10 1803+). Below
    /// this the dock's glass style falls back to an opaque frosted surface.
    /// </summary>
    public static bool SupportsAcrylicBlur() => SupportsAcrylic();

    /// <summary>
    /// Whether the DWM system backdrop (Mica/Acrylic via <c>DWMWA_SYSTEMBACKDROP_TYPE</c>)
    /// is available — Windows 11 22H2 (build 22621) and later. The dock clears this before
    /// applying its own tint-controlled acrylic blur so Windows does not substitute a
    /// grey/white system material.
    /// </summary>
    public static bool SupportsSystemBackdrop()
    {
        var version = Environment.OSVersion.Version;
        return version.Major > 10 || (version.Major == 10 && version.Build >= 22621);
    }
}

