// A direct translation of https://github.com/fsprojects/Fabulous/blob/master/Fabulous.XamarinForms/samples/CounterApp/CounterApp/CounterApp.fs
// I prefer `Try1` over this version.

module CounterApp.Try2

open System
open System.Windows
open System.Windows.Controls
open System.Windows.Media

open System.Reactive.Linq
open System.Reactive.Disposables
open FSharp.Control.Reactive

/// Subscribers
let do' f c = f c; Disposable.Empty
let prop s v c = Observable.subscribe (s c) v
let event s f c = (s c : IEvent<_,_>).Subscribe(fun v -> f c v)
let children clear add set (v1 : IObservable<IObservable<IObservable<_>>>) c = // Note: The previous versions of this have bugs.
    let v2_disp = new SerialDisposable()
    new CompositeDisposable(
        v1.Subscribe(fun v2 ->
            clear c
            v2_disp.Disposable <- 
                let v3_disp = new CompositeDisposable()
                let mutable i = 0
                new CompositeDisposable(
                    v2.Subscribe (fun v3 ->
                        let i' = i
                        v3_disp.Add <| v3.Subscribe (fun v -> if i' < i then set c i' v else i <- add c v + 1)
                        ),
                    v3_disp
                    )
            ),
        v2_disp
        )
    :> IDisposable
let ui_element_collection v1 c = children (fun (c : UIElementCollection) -> c.Clear()) (fun c -> c.Add) (fun c i v -> c.RemoveAt i; c.Insert(i,v)) v1 c

/// Transformers
let control'<'a when 'a :> UIElement> (c : unit -> 'a) l = 
    Observable.Create (fun (sub : IObserver<_>) ->
        let c = c()
        let d = new CompositeDisposable()
        List.iter (fun x -> d.Add(x c)) l
        sub.OnNext(c)
        d :> IDisposable
        )
let control c l = control' c l :?> IObservable<UIElement>

let stack_panel' props childs = control StackPanel (List.append props [fun c -> ui_element_collection childs c.Children])
let stack_panel props childs = stack_panel' props (Observable.ofSeq childs |> Observable.single)
let window props content = control' Window (List.append props [prop (fun t v -> t.Content <- v) content])

/// The example
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

type CmdMsg =
    | TickTimer

let init = { Count = 0; Step = 1; TimerOn=false }

let pump = Subject.broadcast
let dispatch msg = pump.OnNext msg
let update_cmd =
    let init = init, []
    pump
    |> Observable.scanInit init (fun (model,_) msg ->
        match msg with
        | Increment -> { model with Count = model.Count + model.Step }, []
        | Decrement -> { model with Count = model.Count - model.Step }, []
        | Reset -> init
        | SetStep n -> { model with Step = n }, []
        | TimerToggled on -> { model with TimerOn = on }, (if on then [ TickTimer ] else [])
        | TimedTick -> if model.TimerOn then { model with Count = model.Count + model.Step }, [ TickTimer ] else model, [] 
        )
    |> Observable.startWith [init]

let update = Observable.map fst update_cmd

// Interesting tidbit - if you quckly check the checkbox on and off, it is possible to have the time go faster than it should.
// So from that perspective the previous implementation was better.
let cmd_timer =
    Observable.single TimedTick
    |> Observable.delay (TimeSpan.FromSeconds(1.0))

let cmd() =
    update_cmd
    |> Observable.map (snd >> Observable.toObservable)
    |> Observable.concatInner
    |> Observable.bind (function 
        | TickTimer -> cmd_timer
        )
    |> Observable.subscribe (fun x ->
        Application.Current.Dispatcher.Invoke(fun () -> dispatch x)
        )

let view =
    window [ do' (fun t -> t.Title <- "Counter App")]
    <| control Border [
        do' (fun b -> b.Padding <- Thickness 30.0; b.BorderBrush <- Brushes.Black; b.Background <- Brushes.AliceBlue)
        prop (fun b v -> b.Child <- v) <|
            stack_panel [ do' (fun p -> p.VerticalAlignment <- VerticalAlignment.Center)] [
                control Label [
                    do' (fun l -> l.HorizontalAlignment <- HorizontalAlignment.Center; l.HorizontalContentAlignment <- HorizontalAlignment.Center; l.Width <- 50.0)
                    prop (fun l v -> l.Content <- v) (update |> Observable.map (fun model -> sprintf "%d" model.Count))
                    ]
                control Button [
                    do' (fun b -> b.Content <- "Increment"; b.HorizontalAlignment <- HorizontalAlignment.Center)
                    event (fun b -> b.Click) (fun b arg -> dispatch Increment)
                    ]
                control Button [
                    do' (fun b -> b.Content <- "Decrement"; b.HorizontalAlignment <- HorizontalAlignment.Center)
                    event (fun b -> b.Click) (fun b arg -> dispatch Decrement)
                    ]
                control Border [
                    do' (fun b -> b.Padding <- Thickness 20.0)
                    prop (fun b v -> b.Child <- v) <|
                        stack_panel [do' (fun p -> p.Orientation <- Orientation.Horizontal; p.HorizontalAlignment <- HorizontalAlignment.Center)] [
                            control Label [do' (fun l -> l.Content <- "Timer")]
                            control CheckBox [
                                prop (fun c v -> c.IsChecked <- Nullable(v)) (update |> Observable.map (fun model -> model.TimerOn))
                                event (fun c -> c.Checked) (fun c v -> dispatch (TimerToggled true))
                                event (fun c -> c.Unchecked) (fun c v -> dispatch (TimerToggled false))
                                ]
                            ]
                    ]
                control Slider [
                    do' (fun s -> s.Minimum <- 0.0; s.Maximum <- 10.0; s.IsSnapToTickEnabled <- true)
                    prop (fun s v -> s.Value <- v) (update |> Observable.map (fun model -> model.Step |> float))
                    event (fun s -> s.ValueChanged) (fun c v -> dispatch (SetStep (int v.NewValue)))
                    ]
                control Label [
                    do' (fun l -> l.HorizontalAlignment <- HorizontalAlignment.Center)
                    prop (fun l v -> l.Content <- v) (update |> Observable.map (fun model -> sprintf "Step size: %d" model.Step))
                    ]
                control Button [
                    do' (fun b -> b.HorizontalAlignment <- HorizontalAlignment.Center; b.Content <- "Reset")
                    prop (fun b v -> b.IsEnabled <- v) (update |> Observable.map (fun model -> model <> init))
                    event (fun b -> b.Click) (fun b v -> dispatch Reset)
                    ]
                ]
        ]

[<STAThread>]
let main _ = 
    let a = Application()
    use __ = view.Subscribe (fun w -> a.MainWindow <- w; w.Show())
    use __ = cmd()
    a.Run()