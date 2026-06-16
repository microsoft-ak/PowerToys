// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;

namespace Microsoft.PowerToys.FloatingDock.FuzzTests;

public static class FuzzTests
{
    public static void FuzzShortcutText(ReadOnlySpan<byte> input)
    {
        var text = Encoding.UTF8.GetString(input);
        _ = ShortcutResolver.FromText(text);
    }

    public static void FuzzSettingsJson(ReadOnlySpan<byte> input)
    {
        var inputBytes = input.ToArray();
        var folder = Path.Combine(
            Path.GetTempPath(),
            "PowerToysFloatingDockFuzz",
            Convert.ToHexString(SHA256.HashData(inputBytes))[..16]);

        try
        {
            Directory.CreateDirectory(folder);

            File.WriteAllBytes(Path.Combine(folder, "settings.json"), inputBytes);
            File.WriteAllBytes(Path.Combine(folder, "dock.json"), inputBytes);

            var store = new DockSettingsStore(folder);
            _ = store.LoadSettings();
            _ = store.LoadState();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
