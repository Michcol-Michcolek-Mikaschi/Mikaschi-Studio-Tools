using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Narzedzia.Contracts.Localization;
using NarzedziaHost.ViewModels;
using NarzedziaHost.Views;
using Modules.AssetsEditor;
using Modules.ObjectBuilder;
using Modules.LapisItemEditor;
using Modules.OldItemEditor;
using Modules.SpiderConverter;
using Modules.SpriteResizer;
using Modules.SpriteSheetCutter;
using Modules.MapEditor;

namespace NarzedziaHost;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var languagePreferences = _serviceProvider.GetRequiredService<AppLanguagePreferencesStore>();
            LocalizationManager.SetLanguage(languagePreferences.Load());
            var vm = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            var loader = _serviceProvider.GetRequiredService<ModuleLoader>();
            vm.LoadModules(loader.GetModules());

            desktop.MainWindow = new MainWindow { DataContext = vm };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<AppLanguagePreferencesStore>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<ModuleLoader>(sp =>
        {
            var loader = new ModuleLoader();
            loader.RegisterModule(new AssetsEditorModule());
            loader.RegisterModule(new ObjectBuilderModule());
            loader.RegisterModule(new LapisItemEditorModule());
            loader.RegisterModule(new OldItemEditorModule());
            loader.RegisterModule(new SpiderConverterModule());
            loader.RegisterModule(new SpriteResizerModule());
            loader.RegisterModule(new SpriteSheetCutterModule());
            loader.RegisterModule(new MapEditorModule());
            loader.RegisterModule(new Modules.SpriteGenerator.SpriteGeneratorModule());
            return loader;
        });
    }
}
