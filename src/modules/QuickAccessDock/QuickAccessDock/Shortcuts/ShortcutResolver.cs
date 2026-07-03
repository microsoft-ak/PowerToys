// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace QuickAccessDock;

internal static class ShortcutResolver
{
    public static ShortcutItem FromPath(string path)
    {
        var kind = ShortcutKind.File;
        if (Directory.Exists(path))
        {
            kind = ShortcutKind.Folder;
        }
        else if (string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            kind = ShortcutKind.Executable;
        }
        else if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            kind = ShortcutKind.Shortcut;
        }
        else if (string.Equals(Path.GetExtension(path), ".url", StringComparison.OrdinalIgnoreCase))
        {
            kind = ShortcutKind.Url;
        }

        return new ShortcutItem
        {
            Name = GetDisplayName(path),
            Target = path,
            WorkingDirectory = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? string.Empty,
            Kind = kind,
        };
    }

    public static ShortcutItem FromText(string text)
    {
        var trimmed = text.Trim();
        var kind = ShortcutKind.Command;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals("ms-settings", StringComparison.OrdinalIgnoreCase)))
        {
            kind = ShortcutKind.Url;
        }
        else if (trimmed.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
        {
            kind = ShortcutKind.StoreApp;
        }
        else if (trimmed.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            kind = ShortcutKind.Shell;
        }
        else if (File.Exists(trimmed) || Directory.Exists(trimmed))
        {
            return FromPath(trimmed);
        }

        return new ShortcutItem
        {
            Name = CreateDisplayName(trimmed, kind),
            Target = trimmed,
            Kind = kind,
        };
    }

    public static Image GetIcon(ShortcutItem item, bool largeIcon)
    {
        var cachedIcon = LoadCachedIcon(item.IconPath);
        if (cachedIcon is not null)
        {
            return cachedIcon;
        }

        try
        {
            if (File.Exists(item.Target))
            {
                using var icon = DockNativeMethods.GetShellIcon(item.Target, largeIcon) ?? Icon.ExtractAssociatedIcon(item.Target);
                if (icon is not null)
                {
                    return ToBitmap(icon, largeIcon);
                }
            }

            if (Directory.Exists(item.Target))
            {
                using var icon = DockNativeMethods.GetShellIcon(item.Target, largeIcon);
                if (icon is not null)
                {
                    return ToBitmap(icon, largeIcon);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QuickAccessDock] GetIcon failed: {ex}");
        }

        return item.Kind switch
        {
            ShortcutKind.Folder => SystemIcons.WinLogo.ToBitmap(),
            ShortcutKind.Url => SystemIcons.Information.ToBitmap(),
            ShortcutKind.Command => SystemIcons.Shield.ToBitmap(),
            _ => SystemIcons.Application.ToBitmap(),
        };
    }

    private static Image? LoadCachedIcon(string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(iconPath);
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch
        {
            return null;
        }
    }

    private static Image ToBitmap(Icon icon, bool largeIcon)
    {
        if (largeIcon)
        {
            return icon.ToBitmap();
        }

        using var smallIcon = new Icon(icon, 16, 16);
        return smallIcon.ToBitmap();
    }

    // Shell metacharacters that enable command chaining, piping, or redirection.
    // Commands containing these are rejected to prevent unintended side effects.
    private static readonly char[] DangerousCommandChars = ['&', '|', '>', '<', '^', '`'];

    public static void Launch(ShortcutItem item)
    {
        try
        {
            if (item.Kind == ShortcutKind.Command &&
                item.Target.IndexOfAny(DangerousCommandChars) >= 0)
            {
                MessageBox.Show(
                    $"The command \"{item.Target}\" contains shell operators (&, |, >, <, ^, `) and cannot be run directly.{Environment.NewLine}Use a .bat or .ps1 script file for complex commands.",
                    "QuickAccess Dock",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var startInfo = item.Kind == ShortcutKind.Command
                ? new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{item.Target.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    WorkingDirectory = string.IsNullOrWhiteSpace(item.WorkingDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : item.WorkingDirectory,
                }
                : new ProcessStartInfo
                {
                    FileName = item.Target,
                    Arguments = item.Arguments,
                    WorkingDirectory = item.WorkingDirectory,
                    UseShellExecute = true,
                };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open {item.Name}.{Environment.NewLine}{ex.Message}",
                "QuickAccess Dock",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static string GetDisplayName(string path)
    {
        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path).Name;
        }

        var fileName = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
    }

    private static string CreateDisplayName(string target, ShortcutKind kind)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host;
        }

        return kind switch
        {
            ShortcutKind.StoreApp => "Store app",
            ShortcutKind.Shell => target["shell:".Length..],
            ShortcutKind.Command => "Command",
            _ => target,
        };
    }
}

