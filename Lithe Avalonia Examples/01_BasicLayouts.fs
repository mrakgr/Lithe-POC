// Just trying out some basic layouts here.
// Unlike Gtk which is worse than WPF, Avalonia actually seems a bit nicer than it. It is quite similar to WPF in design.
// I had no trouble compiling the observables using the usual pattern just as I'd expected.

// The scheduler, synchronization context and the dispatcher are in Avalonia.Threading.
// I'll make use of that in the next example.
module Avalonia.BasicLayouts

module UI =
    open Avalonia
    open Avalonia.Media
    open Avalonia.Controls
    open Avalonia.Layout

    open System
    open System.Reactive
    open System.Reactive.Linq
    open System.Reactive.Disposables
    open System.Reactive.Concurrency

    /// Lithe
    let do' f c = f c; Disposable.Empty
    let prop s v c = Observable.subscribe (s c) v
    let event s f c = (s c : IEvent<_,_>).Subscribe(fun v -> f c v)

    let control<'a when 'a :> Control> (c : unit -> 'a) l = 
        Observable.Create (fun (obs : IObserver<_>) ->
            let c = c()
            let d = Seq.map ((|>) c) l
            obs.OnNext (c :> Control)
            new CompositeDisposable(d) :> IDisposable
            )

    let children_template (l : Control IObservable seq) (c : Controls) =
        l |> Seq.mapi (fun i v -> v.Subscribe (fun v -> if i < c.Count then c.[i] <- v else c.Add v))
        |> fun x -> new CompositeDisposable(x) :> IDisposable
    let children l (c : #Panel) =  children_template l c.Children
    let content v c = prop (fun (x : #ContentControl) v -> x.Content <- v) v c
    let child v c = prop (fun (x : #Decorator) v -> x.Child <- v) v c

    let window l = control Window l
    let button l = control Button l
    let stack_panel l = control StackPanel l
    let dock_panel l = control DockPanel l
    let slider l = control Slider l
    let text_block l = control TextBlock l
    let border l = control Border l
    let separator l = control Separator l

    /// Example
    let view = 
        window [
            do' (fun x ->
                x.Title <- "Counter Example"
                x.SizeToContent <- SizeToContent.WidthAndHeight
                )
            content <| border [
                do' <| fun x -> 
                    x.BorderBrush <- Brushes.Red; x.BorderThickness <- Thickness 2.0
                child <| stack_panel [
                    children [
                        button [do' <| fun x -> x.Content <- "Click me"; x.FontSize <- 20.0; x.MinWidth <- 1000.0]
                        text_block [do' <| fun x -> x.Text <- "Hello"]
                        separator [do' <| fun x -> x.BorderThickness <- Thickness(2.0); x.BorderBrush <- Brushes.DarkBlue; x.Margin <- Thickness 7.0]
                        border [
                            do' <| fun x -> 
                                x.BorderBrush <- Brushes.Red; x.BorderThickness <- Thickness 2.0
                                x.Padding <- Thickness 10.0; x.Margin <- Thickness(5.0,0.0)
                            child <| dock_panel [
                                children [
                                    text_block [
                                        do' <| fun x -> 
                                            x.Text <- "1: "; x.SetValue(DockPanel.DockProperty, Dock.Top)
                                            x.HorizontalAlignment <- HorizontalAlignment.Center
                                        ]
                                    border [
                                        do' <| fun x -> 
                                            x.BorderBrush <- Brushes.DarkRed; x.BorderThickness <- Thickness 1.0
                                        child <| slider [
                                            do' <| fun x -> 
                                                x.SetValue(DockPanel.DockProperty, Dock.Top)
                                                x.Minimum <- 1.0; x.Maximum <- 10.0; x.TickFrequency <- 1.0; x.IsSnapToTickEnabled <- true
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]

module Main =
    open System
    open Avalonia
    open Avalonia.Controls
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
                UI.view.Add (fun v -> desktop.MainWindow <- v :?> Window)
            | _ -> ()

            base.OnFrameworkInitializationCompleted()

    open Avalonia.Logging.Serilog
    [<CompiledName "BuildAvaloniaApp">] 
    let buildAvaloniaApp () = AppBuilder.Configure<App>().UsePlatformDetect().LogToDebug()

    let main argv = buildAvaloniaApp().StartWithClassicDesktopLifetime(argv)