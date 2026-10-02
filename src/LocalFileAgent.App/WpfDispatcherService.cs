using System;
using System.Windows;
using LocalFileAgent.Application.Search;

namespace LocalFileAgent.App;

public sealed class WpfDispatcherService : IDispatcherService
{
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var app = System.Windows.Application.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }
}
