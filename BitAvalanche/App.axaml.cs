using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;

using BitAvalanche.Core;
using BitAvalanche.Models;
using BitAvalanche.ViewModels;
using BitAvalanche.Views;

namespace BitAvalanche;

public partial class App : Application
{
    public const string SaveDataFile = "data.xml";

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ConfigParser.Parse<Config>("settings.ini");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            desktop.Startup += OnStartup;
            desktop.Exit += OnExit;
            desktop.MainWindow = new TorrentLibrary
            {
                DataContext = new TorrentLibraryViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private void OnStartup(object? sender, ControlledApplicationLifetimeStartupEventArgs e)
    {
        try
        {
            using var f = GetDataFile();
            if (f.Length == 0)
                return;
            var torrents = GetTorrents();
            if (torrents is null)
                return;

            var dcs = new DataContractSerializer(typeof(TorrentTask[]));
            var loaded = (TorrentTask[])dcs.ReadObject(f);
            if (loaded is null)
                return;
            foreach (var t in loaded)
            {
                t.Populate();
                torrents.Add(new TorrentTaskViewModel(t));
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load saved torrents");
            Logger.Error(ex.ToString());
        }
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        try
        {
            var torrents = GetTorrents();
            if (torrents is null)
                return;

            using var f = GetDataFile();
            f.SetLength(0);
            var dcs = new DataContractSerializer(typeof(TorrentTask[]));
            using var xdw = XmlDictionaryWriter.CreateTextWriter(f, Encoding.UTF8);
            dcs.WriteObject(xdw, torrents.Select(t => t.Task).ToArray());
        }
        catch (Exception ex)
        {
            Logger.Error("Could not save torrents");
            Logger.Error(ex.ToString());
        }
    }

    private ObservableCollection<TorrentTaskViewModel>? GetTorrents()
    {
        var lifetime = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (lifetime is null) return null;
        var dataCtx = lifetime.MainWindow?.DataContext;
        if (dataCtx is not TorrentLibraryViewModel torrentVM)
            return null;
        return torrentVM.Torrents;
    }

    private string GetSaveDirectory()
    {
        var appsaveDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var progName = System.AppDomain.CurrentDomain.FriendlyName;
        var dir = Path.Combine(appsaveDir, progName);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return dir;
    }

    private FileStream GetDataFile()
    {
        var saveDir = GetSaveDirectory();
        var filePath = Path.Combine(saveDir, SaveDataFile);
        return File.Open(filePath, FileMode.OpenOrCreate);
    }
}
