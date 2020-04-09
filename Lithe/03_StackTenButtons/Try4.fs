module StackTenButtons.Try4

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
                        v3_disp.Disposable <- v3.Subscribe (fun v -> if i' < i then set c i v else i <- add c v)
                        ),
                    v3_disp
                    )
            ),
        v2_disp
        )
    :> IDisposable

let children' clear add set v1 c = children clear add set (v1 |> Observable.ofSeq |> Observable.single) c

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

let buttons = Subject.broadcast

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
                )
            children' (fun pan -> pan.Children.Clear()) (fun pan -> pan.Children.Add) (fun pan i v -> pan.Children.[i] <- v) [
                control' Button [
                    do' (fun btn -> 
                        btn.Content <- "Create other buttons (10)"
                        btn.Click.Add(fun _ -> buttons.OnNext(create_buttons 10)))
                    ]
                control' Button [
                    do' (fun btn -> 
                        btn.Content <- "Create other buttons (5)"
                        btn.Click.Add(fun _ -> buttons.OnNext(create_buttons 5)))
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
let main _ = 
    let a = Application()
    use __ = w.Subscribe (fun w -> a.MainWindow <- w; w.Show())
    a.Run()
