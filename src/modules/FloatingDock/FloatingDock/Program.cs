// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Microsoft.PowerToys.FloatingDock;

internal static class Program
{
    // Session-scoped single-instance guard: only one dock window may exist at a time,
    // regardless of how many times the helper is launched (Runner, direct/F5 debug, or
    // a leftover instance). Held for the lifetime of the process.
    private const string SingleInstanceMutexName = @"Local\PowerToys_FloatingDock_SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            // Another dock is already running; exit so we never show a second one.
            return;
        }

        var launchArgs = LaunchArgs.Parse(args);

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var form = new DockForm(new DockSettingsStore());
        using var exitSignal = ExitSignal.Open(launchArgs.ExitEventName, () => SafeExit(form));

        MonitorParentProcess(launchArgs.ParentProcessId, form);
        Application.Run(form);
    }

    private static void MonitorParentProcess(int? parentProcessId, Form form)
    {
        if (parentProcessId is null)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                using var parent = Process.GetProcessById(parentProcessId.Value);
                parent.WaitForExit();
                SafeExit(form);
            }
            catch
            {
                SafeExit(form);
            }
        });
    }

    private static void SafeExit(Form form)
    {
        try
        {
            if (form.IsHandleCreated)
            {
                form.BeginInvoke(new Action(Application.Exit));
            }
            else
            {
                Application.Exit();
            }
        }
        catch
        {
            Application.Exit();
        }
    }
}
