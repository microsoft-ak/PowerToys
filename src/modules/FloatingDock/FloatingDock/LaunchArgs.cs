// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.PowerToys.FloatingDock;

internal sealed class LaunchArgs
{
    public int? ParentProcessId { get; private init; }

    public string? ExitEventName { get; private init; }

    public static LaunchArgs Parse(string[] args)
    {
        int? parentProcessId = null;
        string? exitEventName = null;

        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--pid" && index + 1 < args.Length && int.TryParse(args[index + 1], out var pid))
            {
                parentProcessId = pid;
                index++;
            }
            else if (args[index] == "--exit-event" && index + 1 < args.Length)
            {
                exitEventName = args[index + 1];
                index++;
            }
        }

        return new LaunchArgs
        {
            ParentProcessId = parentProcessId,
            ExitEventName = exitEventName,
        };
    }
}
