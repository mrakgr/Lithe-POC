module ABasicExample

open System
open System.Windows
open System.Windows.Controls

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

type Msg =
    | Pressed

type Model = {Pressed : bool }

let init = {Pressed=false}

let pump = Subject.broadcast
let dispatch msg = pump.OnNext msg
let update =
    pump
    |> Observable.scanInit init (fun model msg ->
        match msg with
        | Pressed -> {model with Pressed=true}
        )
    |> Observable.startWith [init]

let view =
    window [ do' (fun t -> t.Title <- "A Basic Example")]
    <| stack_panel [] [
            update
            |> Observable.bind (fun model ->
                if model.Pressed then control Label [do' (fun lbl -> lbl.Content <- "I was pressed")]
                else control Button [
                    do' (fun btn -> btn.Content <- "Press me!")
                    event (fun btn -> btn.Click) (fun btn args -> dispatch Pressed)
                    ]
                )
        ]

[<STAThread>]
let main _ = 
    let a = Application()
    use __ = view.Subscribe (fun w -> a.MainWindow <- w; w.Show())
    a.Run()