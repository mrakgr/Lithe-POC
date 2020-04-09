module StackTenButtons.Try2

open System
open System.Windows
open System.Windows.Controls

open System.Reactive
open System.Reactive.Linq
open System.Reactive.Disposables
open FSharp.Control.Reactive
open System.Reactive.Concurrency

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

let w =
    control Window [
        prop (fun t v -> t.Content <- v) <| control StackPanel [
            do' (fun pan ->
                Observable.range 0 10
                |> Observable.subscribeOn ThreadPoolScheduler.Instance
                |> Observable.observeOn (Application.Current.Dispatcher |> Threading.DispatcherSynchronizationContext |> SynchronizationContextScheduler)
                |> Observable.subscribe (fun x -> pan.Children.Add(Button(Content=sprintf "Button %i" x)) |> ignore)
                |> ignore
                )
            ]
        ]

[<STAThread>]
[<EntryPoint>]
let main _ = 
    let a = Application()
    use __ = w.Subscribe (fun w -> a.MainWindow <- w; w.Show())
    a.Run()
