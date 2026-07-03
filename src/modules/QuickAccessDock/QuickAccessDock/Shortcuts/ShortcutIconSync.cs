// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QuickAccessDock;

internal static class ShortcutIconSync
{
    private const int MaxIconBytes = 2 * 1024 * 1024;
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8),
    };

    public static bool CanSync(ShortcutItem item)
    {
        return item.Kind == ShortcutKind.Url && TryGetWebUri(item.Target, out _);
    }

    public static async Task<bool> TrySyncWebsiteIconAsync(ShortcutItem item, string iconCacheFolder)
    {
        if (!TryGetWebUri(item.Target, out var pageUri))
        {
            return false;
        }

        foreach (var iconUri in await GetCandidateIconsAsync(pageUri))
        {
            var bytes = await TryDownloadBytesAsync(iconUri);
            if (bytes is null)
            {
                continue;
            }

            using var bitmap = TryCreateBitmap(bytes);
            if (bitmap is null)
            {
                continue;
            }

            Directory.CreateDirectory(iconCacheFolder);
            var iconPath = Path.Combine(iconCacheFolder, $"{Hash(pageUri.Host + item.Target)}.png");
            bitmap.Save(iconPath, ImageFormat.Png);
            item.IconPath = iconPath;
            return true;
        }

        return false;
    }

    private static async Task<IReadOnlyList<Uri>> GetCandidateIconsAsync(Uri pageUri)
    {
        var candidates = new List<Uri>();

        try
        {
            var html = await Http.GetStringAsync(pageUri);
            foreach (Match match in Regex.Matches(
                         html,
                         "<link\\b(?=[^>]*\\brel\\s*=\\s*[\"'][^\"']*(?:icon|apple-touch-icon)[^\"']*[\"'])(?=[^>]*\\bhref\\s*=\\s*[\"'](?<href>[^\"']+)[\"'])[^>]*>",
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                var href = match.Groups["href"].Value;
                if (Uri.TryCreate(pageUri, href, out var iconUri) &&
                    (iconUri.Scheme == Uri.UriSchemeHttp || iconUri.Scheme == Uri.UriSchemeHttps) &&
                    !candidates.Any(existing => existing == iconUri))
                {
                    candidates.Add(iconUri);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QuickAccessDock] GetCandidateIconsAsync failed for {pageUri}: {ex.Message}");
        }

        var fallback = new Uri(pageUri, "/favicon.ico");
        if (!candidates.Any(existing => existing == fallback))
        {
            candidates.Add(fallback);
        }

        return candidates;
    }

    private static async Task<byte[]?> TryDownloadBytesAsync(Uri uri)
    {
        try
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode ||
                response.Content.Headers.ContentLength is > MaxIconBytes)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            return bytes.Length <= MaxIconBytes ? bytes : null;
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? TryCreateBitmap(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream);
            return ResizeForDock(image);
        }
        catch
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                using var icon = new Icon(stream);
                using var bitmap = icon.ToBitmap();
                return ResizeForDock(bitmap);
            }
            catch
            {
                return null;
            }
        }
    }

    private static Bitmap ResizeForDock(Image image)
    {
        const int size = 64;
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        var scale = Math.Min((float)size / image.Width, (float)size / image.Height);
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        var x = (size - width) / 2;
        var y = (size - height) / 2;

        graphics.DrawImage(image, new Rectangle(x, y, width, height));
        return bitmap;
    }

    private static bool TryGetWebUri(string target, out Uri uri)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out uri!) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }

        try
        {
            if (File.Exists(target) &&
                string.Equals(Path.GetExtension(target), ".url", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in File.ReadLines(target))
                {
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase) &&
                        Uri.TryCreate(line["URL=".Length..], UriKind.Absolute, out uri!) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
        }

        uri = null!;
        return false;
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()))).ToLowerInvariant();
    }
}

