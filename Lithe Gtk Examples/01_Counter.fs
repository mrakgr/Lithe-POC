// Does not give correct results. Unlike WPF, Gtk requires that the subcontainers be added right away.
// I will have to CPS this.
module Gtk.Counter.Try1

open System
open Gtk

open System.Reactive.Concurrency
open System.Reactive.Linq
open System.Reactive.Disposables

//open FSharp.Control.Reactive

/// Subscribers
let do' f c = f c; Disposable.Empty
let prop s v c = Observable.subscribe (s c) v
let event s f c = (s c : IEvent<_,_>).Subscribe(fun v -> f c v)
type IChildOps<'a,'b> = 
    abstract member clear : 'a -> unit
    abstract member add : 'a -> 'b -> unit
    abstract member set : 'a -> int -> 'b -> unit

let childrent' (d : IChildOps<_,_>) (v1 : IObservable<IObservable<IObservable<_>>>) c =
    let v2_disp = new SerialDisposable()
    new CompositeDisposable(
        v1.Subscribe(fun v2 ->
            d.clear c
            v2_disp.Disposable <- 
                let v3_disp = new CompositeDisposable()
                let mutable i = 0
                new CompositeDisposable(
                    v2.Subscribe (fun v3 ->
                        let i' = i
                        v3_disp.Add <| v3.Subscribe (fun v -> 
                            printfn "Adding %A at position %i" (box v :?> Widget).Name i
                            if i' < i then d.set c i' v else d.add c v; i <- i + 1)
                        ),
                    v3_disp
                    )
            ),
        v2_disp
        )
    :> IDisposable

let childrent d (l : IObservable<_> seq) = l.ToObservable() |> Observable.Return |> childrent' d

let control'<'a when 'a :> Widget> (c : unit -> 'a) l = 
    Observable.Create (fun (sub : IObserver<_>) ->
        let c = c()
        let d = new CompositeDisposable(c)
        List.iteri (fun i x -> 
            printfn "Processing the %ith item for %A" i c.Name
            d.Add(x c)) l
        printfn "Created %A" c.Name
        printfn "Calling OnNext for %A" c.Name; sub.OnNext(c)
        d :> IDisposable
        )
let control c l = control' c l :?> IObservable<Widget>

/// Property setters
let child l = prop (fun (c : Container) v -> c.Child <- v) l
let inline box_children (childrent' : IChildOps<_,_> -> _) l =
    childrent' {new IChildOps<_,_> with
        member _.clear(c: #Box) = c.Foreach(fun x -> c.Remove x)
        member _.add(c: #Box) v = c.Add v
        member _.set(c: #Box) i v' = let v = c.Children.[i] in c.Remove(v); c.Add(v'); c.ReorderChild(v',i)
        } l
let children' l b = box_children childrent' l b
let children l = box_children childrent l

/// Helpers for controls.
let window l = control (fun () -> new Window(null)) l
let vbox' homogeneous spacing l = control (fun () -> new VBox(homogeneous,spacing)) l
let vbox l = vbox' false 0 l
let hseparator l = control HSeparator l
let button l = control Button l
let radio_button l (group : RadioButton) = control' (fun _ -> new RadioButton(group)) l
let radio_button_group (radiobtn_args : (RadioButton -> IDisposable) list list) =
    List.map radio_button radiobtn_args
    |> function
        | [] -> vbox []
        | x_obs :: xs_obs -> 
            (x_obs null).Select(fun btn_leader -> (Observable.Return btn_leader :: List.map ((|>) btn_leader) xs_obs).ToObservable())
            |> children' |> List.singleton |> vbox

let view () = 
    window [
        child <| vbox [
            children [ 
                //radio_button_group [
                //    [do' (fun btn -> btn.Label <- "button 1")]
                //    [do' (fun btn -> btn.Label <- "button 2";  btn.Active <- true)]
                //    [do' (fun btn -> btn.Label <- "button 3")]
                //    ]
                hseparator []
                vbox [
                    //do' (fun box -> box.Spacing <- 10; box.BorderWidth <- 10u)
                    children [
                        button [
                            do' (fun btn -> btn.Label <- "close"; btn.CanDefault <- true; btn.GrabDefault())
                            event (fun btn -> btn.Clicked) (fun _ _ -> Application.Quit())
                            ]
                        ]
                    ]
                ]
            ]
        do' (fun c ->
            printfn "Setting the window settings."
            c.DefaultWidth <- 400
            c.BorderWidth <- 0u
            c.Title <- "Radio buttons"
            )
        event (fun c -> c.DeleteEvent) (fun _ _ -> Application.Quit())
        ]

let main' =
    Application.Init()
    let window = new Window(null)
    use box1 = new VBox (false, 0)
    window.Add(box1)

    use box2 = new VBox (false, 0)
    box1.Add box2
 
    use radiobutton = new RadioButton(null : RadioButton)
    radiobutton.Label <- "button 1"
    use radiobutton2 = new RadioButton(radiobutton)
    radiobutton2.Label <- "button 2";  radiobutton2.Active <- true
    use radiobutton3 = new RadioButton(radiobutton)
    radiobutton3.Label <- "button 3"
    
    box2.Add(radiobutton)
    box2.Add(radiobutton2)
    box2.Add(radiobutton3)
 
    use separator = new HSeparator ()
    box1.Add(separator)
 
    use box3 = new VBox(false, 0)
    
    box1.Add(box3)
 
    use button = new Button ("close")
    use __ = button.Clicked.Subscribe(fun _ -> Application.Quit())
 
    box3.Add(button)
    button.CanDefault <- true
    button.GrabDefault()

    window.DefaultWidth <- 400
    window.BorderWidth <- 0u
    window.ShowAll()
    use __ = window.DeleteEvent.Subscribe(fun x -> Application.Quit())

    Application.Run()
    0

let main _ =
    Application.Init()
    use __ = view().Subscribe(fun x -> printfn "Showing..."; x.ShowAll())
    Application.Run()
    0

