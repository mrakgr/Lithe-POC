module StackTenButtons.Try5

open System
open System.Windows
open System.Windows.Controls

open System.Reactive
open System.Reactive.Linq
open System.Reactive.Disposables
open FSharp.Control.Reactive
open System.Reactive.Concurrency
open System.Windows.Media

let control<'a when 'a :> UIElement> (c : unit -> 'a) l = 
    Observable.Create (fun (sub : IObserver<_>) ->
        let c = c()
        let d = new CompositeDisposable()
        List.iter (fun x -> d.Add(x c)) l
        sub.OnNext(c)
        d :> IDisposable
        )

let control' c l = control c l :?> IObservable<UIElement>
    
let do' f c = f c; Disposable.Empty
let prop s v c = Observable.subscribe (s c) v
let event s f c = (s c : IEvent<_,_>).Subscribe(fun v -> f v)
let children clear add set (v1 : IObservable<IObservable<IObservable<_>>>) c =
    let v2_disp = new SerialDisposable()
    new CompositeDisposable(
        v1.Subscribe(fun v2 ->
            clear c
            v2_disp.Disposable <- 
                let v3_disp = new SerialDisposable()
                let mutable i = 0
                new CompositeDisposable(
                    v2.Subscribe (fun v3 ->
                        let i' = i
                        v3_disp.Disposable <- v3.Subscribe (fun v -> if i' < i then set c i v else i <- add c v) // TODO: Fix this.
                        ),
                    v3_disp
                    )
            ),
        v2_disp
        )
    :> IDisposable

let children' clear add set v1 c = children clear add set (v1 |> Observable.ofSeq |> Observable.single) c

type Messages =
    | SliderChanged of int

type Model = { num_buttons : int }

let rng = Random()
let create_buttons n =
    Observable.range 0 n
    |> Observable.map (fun i ->
        control Button [do' (fun btn ->
            btn.Margin <- Thickness 2.0
            btn.Name <- 'A' + char i |> string
            btn.FontSize <- rng.Next(10) |> float |> (+) btn.FontSize
            btn.Content <- sprintf "Button %s says click me!" btn.Name
            btn.Click.Add(fun args -> MessageBox.Show(sprintf "Button %s has been clicked!" btn.Name,"Button Click") |> ignore)
            )]
        )

let pump = Subject.broadcast
let dispatch msg = pump.OnNext msg
let update =
    let init_model = {num_buttons=0}
    pump
    |> Observable.scanInit init_model (fun model msg ->
        match msg with
        | SliderChanged(n) -> {model with num_buttons=n}
        )
    |> Observable.startWith [init_model]

let buttons =
    update
    |> Observable.map (fun model -> model.num_buttons)
    |> Observable.distinctUntilChanged
    |> Observable.map create_buttons

let view =
    control Window [
        do' (fun t ->
            t.MinHeight <- 300.0
            t.MinWidth <- 300.0
            t.WindowStartupLocation <- WindowStartupLocation.CenterScreen
            t.SizeToContent <- SizeToContent.WidthAndHeight
            t.Title <- "Stack Ten Buttons"
            )
        prop (fun t v -> t.Content <- v) <| control StackPanel [
            do' (fun pan ->
                pan.Background <- Brushes.Aquamarine
                pan.Margin <- Thickness 10.0
                )
            children' (fun pan -> pan.Children.Clear()) (fun pan -> pan.Children.Add) (fun pan i v -> pan.Children.[i] <- v) [
                control' Slider [
                    do' (fun sld -> 
                        sld.Minimum <- 0.0
                        sld.Maximum <- 10.0
                        )
                    event (fun sld -> sld.ValueChanged) (fun x -> dispatch (SliderChanged (int x.NewValue)))
                    ]
                control' StackPanel [
                    do' (fun pan ->
                        pan.Background <- Brushes.Red
                        pan.Margin <- Thickness 20.0)
                    children (fun pan -> pan.Children.Clear()) (fun pan -> pan.Children.Add) (fun pan i v -> pan.Children.[i] <- v) 
                        (buttons.AsObservable())
                    ]
                ]
        ]
    ]

[<STAThread>]
[<EntryPoint>]
let main _ = 
    let a = Application()
    use __ = view.Subscribe (fun w -> a.MainWindow <- w; w.Show())
    a.Run()
