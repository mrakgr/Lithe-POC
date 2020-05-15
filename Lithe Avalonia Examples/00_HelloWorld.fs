module Avalonia.HelloWorld

open Avalonia.Controls
open Avalonia.Layout

type MainWindow () as self = 
    inherit Window ()
    
    let txb = TextBlock(Text = "F# Avalonia app", 
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center)
    
    do self.Content <- txb

open System
open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Markup.Xaml.Styling

type App() =
    inherit Application()

    override x.Initialize() =
        x.Styles.AddRange [ 
            new StyleInclude(baseUri=null, Source = Uri("resm:Avalonia.Themes.Default.DefaultTheme.xaml?assembly=Avalonia.Themes.Default"))
            new StyleInclude(baseUri=null, Source = Uri("resm:Avalonia.Themes.Default.Accents.BaseLight.xaml?assembly=Avalonia.Themes.Default"))
        ]

    override x.OnFrameworkInitializationCompleted() =
        match x.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
             desktop.MainWindow <- new MainWindow()
        | _ -> ()

        base.OnFrameworkInitializationCompleted()

open Avalonia.Logging.Serilog
[<CompiledName "BuildAvaloniaApp">] 
let buildAvaloniaApp () = 
    AppBuilder.Configure<App>().UsePlatformDetect().LogToDebug()

let main argv = buildAvaloniaApp().StartWithClassicDesktopLifetime(argv)