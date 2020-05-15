// The counter example.
module Avalonia.Counter

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

    type Model = {
        Count : int
        Step : int
        TimerOn : bool
        }

    type Msg =
        | Increment
        | Decrement
        | Reset
        | SetStep of int
        | TimerToggled of bool
        | TimedTick

    let init = { Count = 0; Step = 1; TimerOn=false }

    let pump = Subject.Synchronize(Subject(), Avalonia.Threading.AvaloniaScheduler.Instance)
    let dispatch msg = pump.OnNext msg
    let update =
        pump.Scan(init, fun model msg ->
            match msg with
            | Increment -> { model with Count = model.Count + model.Step }
            | Decrement -> { model with Count = model.Count - model.Step }
            | Reset -> init
            | SetStep n -> { model with Step = n }
            | TimerToggled on -> { model with TimerOn = on }
            | TimedTick -> if model.TimerOn then { model with Count = model.Count + model.Step } else model 
            )
            .Publish(init)

    let cmd_timer =
        Observable.DistinctUntilChanged(update, fun x -> x.TimerOn)
            .Select(fun model ->
                if model.TimerOn then Observable.Interval(TimeSpan.FromSeconds(1.0)).Select(fun _ -> TimedTick)
                else Observable.Empty()
                )
            .Switch()

    let cmd() =
        cmd_timer // If there was more than one command handler I'd merge them here.
            .Subscribe(dispatch)

    let view = 
        window [
            do' (fun x -> x.Title <- "Counter Example"; x.SizeToContent <- SizeToContent.WidthAndHeight)
            content <| border [
                do' <| fun x -> x.BorderBrush <- Brushes.Red; x.BorderThickness <- Thickness 2.0
                child <| stack_panel [
                    children [
                        text_block [
                            do' (fun l -> l.HorizontalAlignment <- HorizontalAlignment.Center; l.TextAlignment <- TextAlignment.Center)
                            prop (fun l v -> l.Text <- v) (update.Select(fun model -> sprintf "%d" model.Count))
                            ]
                        button [
                            do' (fun b -> b.Content <- "Increment"; b.HorizontalAlignment <- HorizontalAlignment.Center)
                            event (fun b -> b.Click) (fun b arg -> dispatch Increment)
                            ]
                        button [
                            do' (fun b -> b.Content <- "Decrement"; b.HorizontalAlignment <- HorizontalAlignment.Center)
                            event (fun b -> b.Click) (fun b arg -> dispatch Decrement)
                            ]
                        stack_panel [
                            do' <| fun p -> 
                                p.Orientation <- Orientation.Horizontal
                                p.HorizontalAlignment <- HorizontalAlignment.Center
                                p.Margin <- Thickness 20.0
                            children [
                                text_block [do' (fun l -> l.Text <- "Timer")]
                                check_box [
                                    prop (fun c v -> c.IsChecked <- Nullable(v)) (update.Select(fun model -> model.TimerOn))
                                    event (fun c -> c.Checked) (fun c v -> dispatch (TimerToggled true))
                                    event (fun c -> c.Unchecked) (fun c v -> dispatch (TimerToggled false))
                                    ]
                                ]
                            ]
                        slider [
                            do' (fun s -> s.Minimum <- 0.0; s.Maximum <- 10.0; s.IsSnapToTickEnabled <- true)
                            prop (fun s v -> s.Value <- v) (update.Select(fun model -> model.Step |> float))
                            prop_change Slider.ValueProperty (fun _ v -> dispatch (SetStep (int v)))
                            ]
                        text_block [
                            do' (fun l -> l.HorizontalAlignment <- HorizontalAlignment.Center)
                            prop (fun l v -> l.Text <- v) (update.Select(fun model -> sprintf "Step size: %d" model.Step))
                            ]
                        button [
                            do' (fun b -> b.HorizontalAlignment <- HorizontalAlignment.Center; b.Content <- "Reset")
                            prop (fun b v -> b.IsEnabled <- v) (update.Select(fun model -> model <> init))
                            event (fun b -> b.Click) (fun b v -> dispatch Reset)
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
                UI.cmd()
                UI.update.Connect()
                ] |> List.iter d.Add // TODO: Figure out how to dispose of this when App closes.
            | _ -> ()

            base.OnFrameworkInitializationCompleted()

        interface IDisposable with member _.Dispose() = d.Dispose()

    open Avalonia.Logging.Serilog
    [<CompiledName "BuildAvaloniaApp">] 
    let buildAvaloniaApp () = AppBuilder.Configure<App>().UsePlatformDetect().LogToDebug()

    let main argv = buildAvaloniaApp().StartWithClassicDesktopLifetime(argv)