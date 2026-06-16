// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;
using System.Windows.Forms;

namespace FloatingDock;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\FloatingDock_SingleInstance";

    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.Run(new DockForm(new DockSettingsStore()));
    }
}
