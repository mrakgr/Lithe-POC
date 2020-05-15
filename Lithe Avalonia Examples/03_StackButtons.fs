// The button stacking example. Showcases dynamic layouts.
module Avalonia.StackButtons

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
    open System.Reactive.Subjects

    /// Lithe
    let do' f c = f c; Disposable.Empty
    let prop s v c = Observable.subscribe (s c) v
    let prop_set (p : 'a AvaloniaProperty) (v : 'a IObservable) (c : #Control) = v.Subscribe(fun v -> c.SetValue(p,v))
    let prop_change<'a,'c when 'c :> Control> (p : 'a AvaloniaProperty) f (c : 'c) = c.GetPropertyChangedObservable(p).Subscribe(fun x -> f c (x.NewValue :?> 'a))
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
    let check_box l = control CheckBox l

    /// Example

    type Messages =
        | SliderChanged of int

    type Model = {num_buttons : int }

    let init = {num_buttons = 3}

    let pump = Subject.Synchronize(Subject(), Avalonia.Threading.AvaloniaScheduler.Instance)
    let dispatch msg = pump.OnNext msg
    let update =
        pump.Scan(init, fun model msg ->
            match msg with
            | SliderChanged(n) -> {model with num_buttons=n}
            )
            .Publish(init)

    let num_buttons = update.Select(fun x -> x.num_buttons).DistinctUntilChanged()

    // Displays a message box as a dialog window
    let message_box_dialog msg = 
        match Application.Current.ApplicationLifetime with
        | :? ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime as desktop ->
            let d = new SingleAssignmentDisposable()
            window [
                do' <| fun x -> x.SizeToContent <- SizeToContent.WidthAndHeight; x.Title <- "Message"
                event (fun x -> x.Closing) (fun _ _ -> d.Dispose())
                content <| text_block [
                    do' <| fun x -> 
                        x.Margin <- Thickness 30.0; x.FontSize <- 30.0; 
                        x.Text <- msg
                    ]
                ]
            |> fun w -> d.Disposable <- w.Subscribe(fun x -> (x :?> Window).ShowDialog(desktop.MainWindow) |> ignore)
        | _ -> ()

    let create_buttons =
        let rng = Random()
        fun n -> 
            [0..n-1]
            |> List.map (fun i ->
                let name = 'A' + char i |> string
                button [
                    do' <| fun btn ->
                        btn.Margin <- Thickness 2.0
                        btn.FontSize <- rng.Next(10) |> float |> (+) btn.FontSize
                        btn.Content <- sprintf "Button %s says click me!" name

                    event (fun btn -> btn.Click) (fun btn args -> message_box_dialog <| sprintf "Button %s has been clicked!" name)
                    ]
                )

    let view = 
        window [
            do' <| fun t ->
                t.MinHeight <- 300.0
                t.MinWidth <- 300.0
                t.WindowStartupLocation <- WindowStartupLocation.CenterScreen
                t.SizeToContent <- SizeToContent.WidthAndHeight
                t.Title <- "Stack Ten Buttons"
            content <| stack_panel [
                do' <| fun pan ->
                    pan.Background <- Brushes.Aquamarine
                    pan.Margin <- Thickness 10.0
                
                children [
                    text_block [
                        do' <| fun x -> x.HorizontalAlignment <- HorizontalAlignment.Center
                        prop (fun x v -> x.Text <- v) (num_buttons.Select string)
                        ]

                    slider [
                        do' <| fun sld -> 
                            sld.IsSnapToTickEnabled <- true
                            sld.Minimum <- 0.0
                            sld.Maximum <- 10.0
                        prop (fun c v -> c.Value <- v) (num_buttons.Select float)
                        prop_change Slider.ValueProperty (fun _ x -> dispatch (SliderChanged (int x)))
                        ]

                    num_buttons.SelectMany(fun x ->
                        stack_panel [
                            do' <| fun pan -> pan.Background <- Brushes.Red; pan.Margin <- Thickness 20.0
                            children <| create_buttons x
                            ]
                        )
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

        let d = new Reactive.Disposables.CompositeDisposable()

        override x.Initialize() =
            x.Styles.AddRange [ 
                new StyleInclude(baseUri=null, Source = Uri("resm:Avalonia.Themes.Default.DefaultTheme.xaml?assembly=Avalonia.Themes.Default"))
                new StyleInclude(baseUri=null, Source = Uri("resm:Avalonia.Themes.Default.Accents.BaseLight.xaml?assembly=Avalonia.Themes.Default"))
            ]

        override x.OnFrameworkInitializationCompleted() =
            match x.ApplicationLifetime with
            | :? IClassicDesktopStyleApplicationLifetime as desktop ->
                [
                UI.view.Subscribe (fun v -> desktop.MainWindow <- v :?> Window)
                UI.update.Connect()
                ] |> List.iter d.Add // TODO: Figure out how to dispose of this when App closes.
            | _ -> ()

            base.OnFrameworkInitializationCompleted()

        interface IDisposable with member _.Dispose() = d.Dispose()

    open Avalonia.Logging.Serilog
    [<CompiledName "BuildAvaloniaApp">] 
    let buildAvaloniaApp () = AppBuilder.Configure<App>().UsePlatformDetect().LogToDebug()

    let main argv = buildAvaloniaApp().StartWithClassicDesktopLifetime(argv)