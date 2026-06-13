// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;

namespace Microsoft.PowerToys.FloatingDock;

internal sealed class ExitSignal : IDisposable
{
    private readonly EventWaitHandle? exitEvent;
    private readonly RegisteredWaitHandle? registeredWait;

    private ExitSignal(EventWaitHandle? exitEvent, RegisteredWaitHandle? registeredWait)
    {
        this.exitEvent = exitEvent;
        this.registeredWait = registeredWait;
    }

    public static ExitSignal Open(string? eventName, Action exitAction)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return new ExitSignal(null, null);
        }

        try
        {
            var waitHandle = EventWaitHandle.OpenExisting(eventName);
            var registered = ThreadPool.RegisterWaitForSingleObject(
                waitHandle,
                (_, _) => exitAction(),
                null,
                Timeout.InfiniteTimeSpan,
                true);

            return new ExitSignal(waitHandle, registered);
        }
        catch
        {
            return new ExitSignal(null, null);
        }
    }

    public void Dispose()
    {
        registeredWait?.Unregister(null);
        exitEvent?.Dispose();
    }
}
