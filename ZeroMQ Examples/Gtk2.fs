module Gtk.Try2

open System
open Gtk

open System.Reactive.Concurrency
open System.Reactive.Linq
open System.Reactive.Disposables

type LitheControl = IObservable<(Widget -> unit) -> unit>
let control<'a, 'r when 'a :> Widget> (c : unit -> 'a) (l : ('a -> IDisposable) list) : LitheControl = 
    Observable.Create (fun (sub : IObserver<_>) ->
        let d' = new SerialDisposable()
        sub.OnNext(fun p ->
            let c = c()
            let d = new CompositeDisposable(c)
            d'.Disposable <- d
            p (c :> Widget)
            List.iter (fun x -> d.Add(x c)) l
            )
        d' :> IDisposable
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
let children (l : LitheControl seq) c = children' (l.ToObservable() |> Observable.Return) c

/// Helpers for controls.
let window l = control (fun () -> new Window(null)) l
let button l = control Button l

//let view () = 
//    window [
//        button [
//            //do' (fun btn -> btn.Label <- "close"; btn.CanDefault <- true; btn.GrabDefault())
//            //event (fun btn -> btn.Clicked) (fun _ _ -> Application.Quit())
//            ]
//        ]