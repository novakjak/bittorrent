using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

using Out = System.Collections.Generic.IEnumerable<Avalonia.Platform.Storage.IStorageFile>;

namespace bittorrent.Core;

public sealed class FileDialogInteraction : IDisposable, ICommand
{
    private Func<Task<Out>>? _handler;

    public Task<Out> Handle()
    {
        if (_handler is null)
        {
            throw new InvalidOperationException("No handler was supplied");
        }
        return _handler();
    }

    public IDisposable RegisterHandler(Func<Task<Out>> handler)
    {
        _handler = handler;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        return this;
    }

    public void Dispose()
    {
        _handler = null;
    }

    public bool CanExecute(object? param) => _handler is not null;
    public void Execute(object? param) => Handle();
    public event EventHandler? CanExecuteChanged;
}
