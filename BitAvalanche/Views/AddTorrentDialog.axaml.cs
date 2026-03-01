using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

using BitAvalanche.ViewModels;

namespace BitAvalanche.Views;

public partial class AddTorrentDialog : Window
{
    private AddTorrentDialogViewModel Ctx => (AddTorrentDialogViewModel)DataContext!;

    public AddTorrentDialog()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        Ctx.CloseRequested += (_, _) => Close(false);
        base.OnDataContextChanged(e);
    }

    private async void ChangeSaveLocation(object? sender, RoutedEventArgs args)
    {
        var storageProvider = TopLevel.GetTopLevel(this)!
            .StorageProvider;
        var folder = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = "Select save directory",
            });
        if (folder is null || folder.Count() == 0)
            return;
        Ctx.SaveLocation = folder.First();
    }

    private void CloseDialog(object? sender, RoutedEventArgs args)
    {
        Close(false);
    }

    private void Confirm(object? sender, RoutedEventArgs args)
    {
        Close(true);
    }
}
