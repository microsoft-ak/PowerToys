// Copyright (c) 2026 ajaykontham
// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace QuickAccessDock;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\PowerToysQuickAccessDock_SingleInstance";

    // Signaled by the PowerToys runner when the module is disabled or PowerToys exits.
    // Must match CommonSharedConstants::QUICK_ACCESS_DOCK_EXIT_EVENT in
    // src/common/interop/shared_constants.h.
    private const string ExitEventName = @"Local\PowerToysQuickAccessDockExitEvent-8f2c1d0a-6b4e-4f7a-9c3d-2e1b5a7f6d90";

    private static EventWaitHandle? exitEvent;
    private static RegisteredWaitHandle? registeredExitWait;

    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // When launched by the PowerToys runner, exit cleanly if the runner goes away so the
        // dock never outlives its parent, and honor the disable/exit signal from the runner.
        MonitorParentProcess(args);
        RegisterExitEvent();

        Application.ApplicationExit += (_, _) =>
        {
            registeredExitWait?.Unregister(null);
            exitEvent?.Dispose();
        };

        Application.Run(new DockForm(new DockSettingsStore()));
    }

    // The runner passes "--pid <runnerProcessId>". If that process exits, so should the dock.
    private static void MonitorParentProcess(string[] args)
    {
        var pid = ParsePid(args);
        if (pid is null)
        {
            return;
        }

        try
        {
            var parent = Process.GetProcessById(pid.Value);
            parent.EnableRaisingEvents = true;
            parent.Exited += (_, _) => ExitApplication();
        }
        catch (ArgumentException)
        {
            // The parent already exited between launch and here; nothing to keep the dock alive for.
            ExitApplication();
        }
    }

    private static void RegisterExitEvent()
    {
        exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ExitEventName);
        registeredExitWait = ThreadPool.RegisterWaitForSingleObject(
            exitEvent,
            static (_, _) => ExitApplication(),
            state: null,
            millisecondsTimeOutInterval: -1,
            executeOnlyOnce: true);
    }

    private static int? ParsePid(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--pid", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
            {
                return pid;
            }
        }

        return null;
    }

    private static void ExitApplication()
    {
        var form = Application.OpenForms.Cast<Form>().FirstOrDefault();
        if (form is not null && form.IsHandleCreated && !form.IsDisposed)
        {
            form.BeginInvoke(Application.Exit);
        }
        else
        {
            Application.Exit();
        }
    }
}
