// Stack buttons example
// Gtk is just so bad at doing dynamic layouts.
// I got this example to work, but I seriously give up on trying to make it work well.
// I can't do it. These frameworks might seem similar at first glance, but you can really
// feel Gtk's age here.
module Gtk.Try3

open System
open Gtk

open System.Reactive
open System.Reactive.Concurrency
open System.Reactive.Linq
open System.Reactive.Disposables
open System.Reactive.Subjects

// Unlike WPF, Gtk is extremely finicky about order so I had to CPS everything even more than usual.
// The reason it is like this is so I can link the containers before their children get added to them.
type LitheControl = IObservable<(Widget -> unit) -> unit>

let control_template on_next : LitheControl =
    Observable.Create (fun (sub : IObserver<_>) ->
        let d' = new SerialDisposable()
        sub.OnNext(fun set -> d'.Disposable <- on_next set)
        d' :> IDisposable
        )

let control<'a when 'a :> Widget> (c : unit -> 'a) (l : ('a -> IDisposable) seq) : LitheControl = 
    control_template (fun set ->
        let c = c()
        set (c :> Widget)
        // If you are doing dynamic layouts like in this example, you need to Show the controls, otherwise they won't show up.
        // But the results from doing it like this vs showing them all at the end are significantly different.
        c.Show()
        new CompositeDisposable(Seq.map ((|>) c) l)
        )

let child_template set (l : LitheControl) = l.Subscribe(fun f -> f set)
/// Don't need the complicated 3-layered version of this thanks to SelectMany.
let children_template set (l : LitheControl seq) =
    new CompositeDisposable(l |> Seq.mapi (fun i' v3 -> v3.Subscribe (fun f -> f (set i')))) :> IDisposable

let do' f c = f c; Disposable.Empty
let event s f c = (s c : IEvent<_,_>).Subscribe(fun v -> f c v)
let prop s v c = Observable.subscribe (s c) v

let child (l : LitheControl) (c : #Container) = child_template (fun v -> c.Child <- v) l
let children l (c : #Box) =
    let set i (v' : Widget) = 
        if i < c.Children.Length then 
            let v = c.Children.[i]
            c.Remove(v); c.Add(v'); c.ReorderChild(v',i)
        else
            c.Add(v')
    children_template set l

/// Helpers for controls.
let window l = control (fun () -> new Window(null)) l
let button l = control Button l
let vbox' homogeneous spacing l = control (fun () -> new VBox(homogeneous,spacing)) l
let vbox l = vbox' false 0 l
let hseparator l = control HSeparator l
let radio_button l (group : RadioButton) = control (fun _ -> new RadioButton(group)) l
let radio_button_group (radiobtn_args : (RadioButton -> IDisposable) list list): LitheControl =
    control_template (fun set ->
        let box = new VBox(false,0)
        set (box :> Widget)
        box.Show()
        let btn_disposables = 
            List.mapFold (fun (prev : RadioButton) l ->
                let btn = new RadioButton(prev)
                box.Add btn
                btn.Show()
                (btn :> IDisposable) :: List.map ((|>) btn) l, btn
                ) null radiobtn_args 
            |> fst |> List.concat
        new CompositeDisposable((box :> IDisposable) :: btn_disposables)
        )
let slider l = control (fun () -> new Scale(Orientation.Horizontal,1.0,10.0,1.0)) l

/// Example
type Messages =
    | SliderChanged of int

type Model = {num_buttons : int }

let rng = Random()
let create_buttons n =
    [1..n] 
    |> List.map (fun i -> button [
        do' (fun btn ->
            btn.Margin <- 2
            btn.Name <- 'A' + char (i - 1) |> string
            // I wanted to change the font here, but this is actually difficult in Gtk.
            btn.Label <- sprintf "Button %s says click me!" btn.Name)
        ])

let pump = Subject()
let dispatch msg = Application.Invoke(EventHandler(fun _ _ -> pump.OnNext msg))
let update =
    let init_model = {num_buttons=1}
    pump.Scan(init_model, fun model msg ->
        match msg with
        | SliderChanged(n) -> {model with num_buttons=n}
        )
        .Publish(init_model)

let num_buttons = update.Select (fun x -> x.num_buttons) |> Observable.DistinctUntilChanged

let view () = 
    window [
        do' (fun w ->
            w.Title <- "Stack buttons"
            w.Resize(400,200)
            w.BorderWidth <- 0u
            )
        event (fun c -> c.DeleteEvent) (fun _ _ -> Application.Quit())
        child <| vbox [
            children [
                slider [
                    prop (fun x v -> x.Value <- float v) num_buttons
                    event (fun x -> x.ChangeValue) (fun x _ -> x.Value |> int |> SliderChanged |> dispatch)
                    ]
                hseparator [do' (fun x -> x.Margin <- 10; x.HeightRequest <- 5)]
                num_buttons.SelectMany (fun i ->
                    vbox [children (create_buttons i)]
                    )
                ]
            ]
        ]

let try_view () =
    Application.Init()
    use __ = view().Subscribe((|>) ignore)
    use __ = update.Connect()
    Application.Run()
    0

[<EntryPoint>]
let main _ = try_view()
