module StackTenButtons.Try6

open System
open System.Windows
open System.Windows.Controls

open System.Reactive
open System.Reactive.Linq
open System.Reactive.Disposables
open FSharp.Control.Reactive
open System.Reactive.Concurrency
open System.Windows.Media

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

type Messages =
    | SliderChanged of int

type Model = {num_buttons : int }

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

let rng = Random()
let create_buttons n =
    Observable.range 0 n
    |> Observable.map (fun i ->
        control' Button [
            do' (fun btn ->
                btn.Margin <- Thickness 2.0
                btn.Name <- 'A' + char i |> string
                btn.FontSize <- rng.Next(10) |> float |> (+) btn.FontSize
                btn.Content <- sprintf "Button %s says click me!" btn.Name)
            event (fun btn -> btn.Click) (fun btn args -> MessageBox.Show(sprintf "Button %s has been clicked!" btn.Name,"Button Click") |> ignore)
            ]
        )

let view =
    window [
        do' (fun t ->
            t.MinHeight <- 300.0
            t.MinWidth <- 300.0
            t.WindowStartupLocation <- WindowStartupLocation.CenterScreen
            t.SizeToContent <- SizeToContent.WidthAndHeight
            t.Title <- "Stack Ten Buttons"
            )
        ]
    <| stack_panel [
        do' (fun pan ->
            pan.Background <- Brushes.Aquamarine
            pan.Margin <- Thickness 10.0
            )
        ] [
        control Slider [
            do' (fun sld -> 
                sld.IsSnapToTickEnabled <- true
                sld.Minimum <- 0.0
                sld.Maximum <- 10.0
                )
            event (fun sld -> sld.ValueChanged) (fun _ x -> dispatch (SliderChanged (int x.NewValue)))
            ]

        stack_panel' [
            do' (fun pan ->
                pan.Background <- Brushes.Red
                pan.Margin <- Thickness 20.0)
            ] (update
                |> Observable.map (fun model -> model.num_buttons)
                |> Observable.distinctUntilChanged
                |> Observable.map create_buttons)
        ]

[<STAThread>]
[<EntryPoint>]
let main _ = 
    let a = Application()
    use __ = view.Subscribe (fun w -> a.MainWindow <- w; w.Show())
    a.Run()
