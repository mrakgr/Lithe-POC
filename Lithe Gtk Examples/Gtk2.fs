// Based on the radio button example from the Gtk# documentation.
module Gtk.Try2

open System
open Gtk

open System.Reactive.Concurrency
open System.Reactive.Linq
open System.Reactive.Disposables

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
        new CompositeDisposable(Seq.map ((|>) c) l)
        )

let child_template set (l : LitheControl) = l.Subscribe(fun f -> f set)
let children_template clear set (v1 : IObservable<IObservable<LitheControl>>) =
    let v2_disp = new SerialDisposable()
    new CompositeDisposable(
        v1.Subscribe(fun v2 ->
            clear()
            v2_disp.Disposable <- 
                let v3_disp = new CompositeDisposable()
                let mutable i = 0
                new CompositeDisposable(
                    v2.Subscribe (fun v3 ->
                        let i' = i
                        v3_disp.Add <| v3.Subscribe (fun f -> f (set i'); i <- i + 1)
                        ),
                    v3_disp
                    )
            ),
        v2_disp
        )
    :> IDisposable

let do' f c = f c; Disposable.Empty
let event s f c = (s c : IEvent<_,_>).Subscribe(fun v -> f c v)
let prop s v c = Observable.subscribe (s c) v

let child (l : LitheControl) (c : #Container) = child_template (fun v -> c.Child <- v) l
let children' l (c : #Box) =
    let clear () = c.Foreach(fun x -> c.Remove x)
    let set i (v' : Widget) = 
        if i < c.Children.Length then 
            let v = c.Children.[i]
            c.Remove(v); c.Add(v'); c.ReorderChild(v',i)
        else
            c.Add(v')
    children_template clear set l

let children (l : LitheControl seq) c = 
    // I have to use the immediate scheduler as Gtk is really finicky about order.
    children' (l.ToObservable(ImmediateScheduler.Instance) |> Observable.Return) c

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
        let btn_disposables = 
            List.mapFold (fun (prev : RadioButton) l ->
                let btn = new RadioButton(prev)
                box.Add btn
                (btn :> IDisposable) :: List.map ((|>) btn) l, btn
                ) null radiobtn_args 
            |> fst |> List.concat
        new CompositeDisposable((box :> IDisposable) :: btn_disposables)
        )

let view () = 
    window [
        child <| vbox [
            children [ 
                radio_button_group [
                    [do' (fun btn -> btn.Label <- "button 1")]
                    [do' (fun btn -> btn.Label <- "button 2";  btn.Active <- true)]
                    [do' (fun btn -> btn.Label <- "button 3")]
                    ]
                hseparator [do' (fun s -> s.Margin <- 10)]
                button [
                    do' (fun btn -> btn.Label <- "close"; btn.CanDefault <- true; btn.GrabDefault())
                    event (fun btn -> btn.Clicked) (fun _ _ -> Application.Quit())
                    ]
                ]
            ]
        do' (fun w ->
            w.Title <- "Radio buttons"
            w.DefaultWidth <- 400
            w.BorderWidth <- 0u
            w.ShowAll()
            )
        event (fun c -> c.DeleteEvent) (fun _ _ -> Application.Quit())
        ]

let try_view () =
    Application.Init()
    use __ = view().Subscribe((|>) ignore)
    Application.Run()
    0

let main _ = try_view ()
