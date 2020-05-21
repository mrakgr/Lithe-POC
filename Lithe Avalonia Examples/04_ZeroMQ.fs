module Avalonia.ZeroMQ

module Lithe = 
    open Avalonia
    open Avalonia.Media
    open Avalonia.Controls
    open Avalonia.Layout

    open System
    open System.Reactive
    open System.Reactive.Linq
    open System.Reactive.Disposables
    open System.Reactive.Concurrency
    open System.Reactive.Subjects
    open FSharp.Control.Reactive

    let do' f c = f c; Disposable.Empty
    let prop s v c = Observable.subscribe (s c) v
    let prop_set (p : 'a AvaloniaProperty) (v : 'a IObservable) (c : #Control) = v.Subscribe(fun v -> c.SetValue(p,v))
    let prop_change<'a,'c when 'c :> Control> (p : 'a AvaloniaProperty) f (c : 'c) = c.GetPropertyChangedObservable(p).Subscribe(fun x -> f c (x.NewValue :?> 'a))
    let event s f c = (s c : IEvent<_,_>).Subscribe(fun v -> f c v)

    let control<'a when 'a :> Control> (c : unit -> 'a) l = 
        Observable.Create (fun (obs : IObserver<_>) ->
            let c = c()
            let d = Seq.map ((|>) c) l
            obs.OnNext (c :> Control)
            new CompositeDisposable(d) :> IDisposable
            )

    let children (l : Control IObservable seq) (c : #Panel) = 
        let c = c.Children
        l |> Seq.mapi (fun i v -> v.Subscribe (fun v -> if i < c.Count then c.[i] <- v else c.Add v))
        |> fun x -> new CompositeDisposable(x) :> IDisposable
    let items (l : (TabItem -> IDisposable) seq seq) (t : #TabControl) = 
        let d = new CompositeDisposable()
        // t.Items <- x needs to come last otherwise selection won't work.
        // Seq.toArray needs to be here in order to force evaluation.
        l |> Seq.toArray |> Array.map (fun l -> 
            let x = TabItem()
            Seq.iter ((|>) x >> d.Add) l
            x)
        |> fun x -> t.Items <- x; d :> IDisposable
    let content v c = prop (fun (x : #ContentControl) v -> x.Content <- v) v c
    let child v c = prop (fun (x : #Decorator) v -> x.Child <- v) v c

    let window l = control Window l
    let button l = control Button l
    let stack_panel l = control StackPanel l
    let dock_panel l = control DockPanel l
    let slider l = control Slider l
    let text_block l = control TextBlock l
    let text_box l = control TextBox l
    let border l = control Border l
    let separator l = control Separator l
    let check_box l = control CheckBox l
    let tab_control l = control TabControl l
    let tab_item l = control TabItem l
    let scroll_viewer l = control ScrollViewer l
    let list_box l = control ListBox l
    let list_box_item l = control ListBoxItem l
    let empty = Observable.empty : Control IObservable
    type GridUnit = A of float | S of float | P of float
    let vert_grid (l' : (Grid -> IDisposable) seq) (l : (GridUnit * Control IObservable) seq) =
        Observable.Create (fun (obs : _ IObserver) ->
            let c = Grid()
            let d = new CompositeDisposable()
            Seq.iter ((|>) c >> d.Add) l'
            l |> Seq.iteri (fun i (s, x) ->
                let f a b = RowDefinition(a,b)
                match s with A s -> f s GridUnitType.Auto | S s -> f s GridUnitType.Star | P s -> f s GridUnitType.Pixel
                |> c.RowDefinitions.Add
                x.Subscribe(fun x -> 
                    if i < c.Children.Count then c.Children.[i] <- x else c.Children.Add(x)
                    Grid.SetRow(x,i)
                    ) |> d.Add
                )
            obs.OnNext(c)
            new CompositeDisposable(d) :> IDisposable
            )

module Messaging =
    open System
    open System.IO
    open System.Threading
    open System.Threading.Tasks
    open NetMQ
    open NetMQ.Sockets
    open System.Reactive.Disposables

    module HelloWorld =
        module private Inner =
            let uri = "ipc://hello-world"
            let server (log : string -> unit) (poller: NetMQPoller) =
                use server = new ResponseSocket()
                poller.Add(server)
                log <| sprintf "Server is binding to: %s" uri
                server.Bind(uri)
                log <| "Done binding."
                use __ = server.ReceiveReady.Subscribe(fun x ->
                    let x = server.ReceiveFrameString()
                    log (sprintf "Server received %s" x)
                    Thread.Sleep(1000)
                    let msg = sprintf "%s World" x
                    log (sprintf "Server sending %s" msg)
                    server.SendFrame(msg)
                    )
                poller.Run()
                server.Unbind(uri)

            let client (log : string -> unit) (poller: NetMQPoller)  =
                use client = new RequestSocket()
                poller.Add(client)
                log <| sprintf "Client is connecting to: %s" uri
                client.Connect(uri)
                log <| "Done connecting."
                let i = ref 0
                use __ = client.SendReady.Subscribe(fun _ -> 
                    if !i < 3 then
                        let msg = "Hello"
                        msg |> sprintf "Client sending %s" |> log 
                        client.SendFrame(msg)
                        incr i
                    )
                use __ = client.ReceiveReady.Subscribe(fun _ -> 
                    client.ReceiveFrameString() |> sprintf "Client received %s" |> log
                    )
                poller.Run()
                client.Disconnect(uri)

            let run f log = let poller = new NetMQPoller() in poller, Task.Run(fun () -> try f log poller with e -> printfn "%A" e)
            let dispose state =
                state |> Option.iter (fun l -> 
                    let pollers, tasks = Array.unzip l
                    pollers |> Array.iter (fun (x : NetMQPoller) -> x.Stop())
                    Task.WaitAll(tasks)
                    )

        open Inner
        type Msg = Start of server_log : (string -> unit) * client_log : (string -> unit)
        let agent = MailboxProcessor.Start(fun mailbox ->
            let rec loop state = async {
                let! (Start(server_log, client_log)) = mailbox.Receive()
                dispose state
                return! Some [|run server server_log; run client client_log|] |> loop
                }
            loop None
            )
    
    //module Weather =
            

module UI =
    open Lithe
    open Avalonia.Media
    open Avalonia.Controls
    open Avalonia.Layout
    open Avalonia.Controls.Primitives

    open System
    open System.Reactive
    open System.Reactive.Linq
    open System.Reactive.Disposables
    open System.Reactive.Concurrency
    open System.Reactive.Subjects
    open FSharp.Control.Reactive

    let ui_scheduler = Avalonia.Threading.AvaloniaScheduler.Instance

    module HelloWorld =
        let server_stream = Subject.Synchronize(Subject.broadcast,ui_scheduler)
        let client_stream = Subject.Synchronize(Subject.broadcast,ui_scheduler)
        
        type Msg = AddServer of string | AddClient of string | StartExample
        type State = {server_count : int; client_count : int}
        let state_stream = new Subjects.ReplaySubject<_>(1,ui_scheduler)
        open Messaging.HelloWorld
        let agent = MailboxProcessor.Start(fun mailbox ->
            let rec loop (model : State) = async {
                let update (f : _ ISubject) i x = f.OnNext(sprintf "%i: %s" i x); i+1
                state_stream.OnNext(model)
                match! mailbox.Receive() with
                | AddServer x -> return! loop {model with server_count=update server_stream model.server_count x}
                | AddClient x -> return! loop {model with client_count=update client_stream model.client_count x}
                | StartExample -> agent.Post(Start(AddServer >> mailbox.Post, AddClient >> mailbox.Post)); return! loop model
                }
            loop {server_count=0; client_count=0}
            )
        let dispatch msg = agent.Post msg

        let view = 
            let text_list obs =
                border [
                    do' <| fun x -> x.BorderBrush <- Brushes.Black; x.BorderThickness <- Thickness 0.5
                    child <| scroll_viewer [
                        content <| stack_panel [prop (fun x v -> x.Children.Add(TextBlock(Text=v))) obs]
                        prop (fun x _ -> x.Offset <- x.Offset.WithY(infinity)) obs
                        ]
                    ]
            vert_grid [] [
                A 1.0, button [
                    do' <| fun x -> x.Content <- "Run Server & Client"
                    event (fun x -> x.Click) (fun _ _ -> dispatch StartExample)
                    ]
                A 1.0, text_block [do' <| fun x -> x.Text <- "Server:"]
                S 1.0, text_list server_stream
                A 1.0, text_block [do' <| fun x -> x.Text <- "Client:"]
                S 1.0, text_list client_stream
                ]
    
    let view = 
        window [
            do' <| fun t ->
                t.Height <- 600.0
                t.Width <- 700.0
                t.WindowStartupLocation <- WindowStartupLocation.CenterScreen
                t.Title <- "ZeroMQ Examples"
            content <| tab_control [
                items [
                    [do' (fun x -> x.Header <- "Hello World"); content HelloWorld.view]
                    ]
                ]
            ]

module Main =
    open System
    open Avalonia
    open Avalonia.Controls
    open Avalonia.Controls.ApplicationLifetimes
    open Avalonia.Markup.Xaml.Styling

    type App() =
        inherit Application()

        override x.Initialize() =
            x.Styles.AddRange [ 
                new StyleInclude(baseUri=null, Source=Uri("resm:Avalonia.Themes.Default.DefaultTheme.xaml?assembly=Avalonia.Themes.Default"))
                new StyleInclude(baseUri=null, Source=Uri("resm:Avalonia.Themes.Default.Accents.BaseLight.xaml?assembly=Avalonia.Themes.Default"))
            ]

        override x.OnFrameworkInitializationCompleted() =
            match x.ApplicationLifetime with
            | :? IClassicDesktopStyleApplicationLifetime as desktop ->
                UI.view.Add (fun v -> desktop.MainWindow <- v :?> Window)
            | _ -> ()

            base.OnFrameworkInitializationCompleted()

    open Avalonia.Logging.Serilog
    [<CompiledName "BuildAvaloniaApp">] 
    let buildAvaloniaApp () = AppBuilder.Configure<App>().UsePlatformDetect().LogToDebug()

    let main argv = buildAvaloniaApp().StartWithClassicDesktopLifetime(argv)