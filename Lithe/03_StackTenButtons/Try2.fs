module StackTenButtons.Try2

open System
open System.Windows
open System.Windows.Input
open System.Windows.Controls
open System.Windows.Controls.Primitives
open System.Windows.Media
open System.Windows.Media.Imaging
open System.Windows.Shapes
open System.Windows.Documents

open System.Reactive
open System.Reactive.Linq
open System.Reactive.Disposables

open FSharp.Control.Reactive

//let dispatch' = Subject.broadcast
//let dispatch x = dispatch'.OnNext x

// TODO: Forget about cleanup for the time being. This is a proof of concept.
let control c l = 
    Observable.Create (fun (sub : IObserver<_>) ->
        let c = c()
        let d = new CompositeDisposable()
        List.iter (fun x -> d.Add(x c)) l
        sub.OnNext(c)
        d :> IDisposable
        )
let do' f c = f c; Disposable.Empty
let prop s v c = Observable.subscribe (s c) v
let prop_seq s v c = Observable.subscribe (s c) v
let event s f c = (s c : IEvent<'a,_>).Subscribe(f c)
let children' s l c = (s c) l; Disposable.Empty

let w =
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
                Observable.ofSeq [1;2;3]
                |> Observable.subscribe (fun x -> pan.Children.Add(Button(Content=sprintf "Button %i" x)) |> ignore)
                |> ignore
                )
            ]
        ]

[<STAThread>]
[<EntryPoint>]
let main _ = w.Subscribe (Application().Run >> ignore); 0