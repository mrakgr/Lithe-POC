module Avalonia.ZeroMQ

open System
open System.Threading

module Lithe = 
    open Avalonia
    open Avalonia.Controls

    open System
    open System.Reactive.Linq
    open System.Reactive.Disposables
    open FSharp.Control.Reactive

    let subscribe_composite (f : 'a -> #IDisposable) (x : 'a IObservable) =
        let d = new CompositeDisposable()
        new CompositeDisposable(x.Subscribe(fun x -> d.Add(f x)), d) :> IDisposable
    let subscribe_serial (f : 'a -> #IDisposable) (x : 'a IObservable) =
        let d = new SerialDisposable()
        new CompositeDisposable(x.Subscribe(fun x -> d.Disposable <- f x), d) :> IDisposable

    let do' f c = f c; Disposable.Empty
    let connect (v : _ IObservable) c = v.Subscribe()
    let prop s v c = Observable.subscribe (s c) v
    let prop_set (p : 'a AvaloniaProperty) (v : 'a IObservable) (c : #Control) = v.Subscribe(fun v -> c.SetValue(p,v))
    let prop_change<'a,'c when 'c :> Control> (p : 'a AvaloniaProperty) f (c : 'c) = c.GetPropertyChangedObservable(p).Subscribe(fun x -> f c (x.NewValue :?> 'a))
    let event s f c = (s c : IEvent<_,_>).Subscribe(fun v -> f c v)

    let control<'a when 'a :> Control> (c : unit -> 'a) l = 
        Observable.Create (fun (obs : IObserver<_>) ->
            let c = c()
            let d = List.map ((|>) c) l
            obs.OnNext (c :> Control)
            new CompositeDisposable(d) :> IDisposable
            )

    let children (l : Control IObservable list) (c : #Panel) = 
        let c = c.Children
        l |> List.mapi (fun i v -> v.Subscribe (fun v -> if i < c.Count then c.[i] <- v else c.Add v))
        |> fun x -> new CompositeDisposable(x) :> IDisposable
    let items (l : (TabItem -> IDisposable) list list) (t : #TabControl) = 
        let d = new CompositeDisposable()
        // `t.Items <- x` needs to come last otherwise selection won't work.
        l |> List.map (fun l -> 
            let x = TabItem()
            List.iter ((|>) x >> d.Add) l
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
    let private conv f = function A s -> f (s, GridUnitType.Auto) | S s -> f (s, GridUnitType.Star) | P s -> f (s, GridUnitType.Pixel)
    let cd (l : GridUnit list) = let c = ColumnDefinitions() in List.iter (conv ColumnDefinition >> c.Add) l; c
    let cd_set l = do' <| fun (x : #Grid) -> x.ColumnDefinitions <- cd l
    let rd (l : GridUnit list) = let c = RowDefinitions() in List.iter (conv RowDefinition >> c.Add) l; c
    let rd_set l = do' <| fun (x : #Grid) -> x.RowDefinitions <- rd l
        
    // Variable number of rows, fixed number of columns.
    let vert_grid' (l' : (Grid -> IDisposable) list) (l : (GridUnit * #Control IObservable list) IObservable) =
        Observable.Create (fun (obs : _ IObserver) ->
            let c = Grid()

            let d = new CompositeDisposable()
            l' |> List.iter ((|>) c >> d.Add)
            let i_row = ref 0
            l |> subscribe_composite (fun (s, row) -> 
                let incr x = let q = !x in incr x; q
                let i_row = incr i_row
                c.RowDefinitions.Add(conv RowDefinition s)

                row |> List.mapi (fun i_col col ->
                    let i_child = c.Children.Count
                    col.Subscribe(fun x ->
                        if i_child < c.Children.Count then c.Children.[i_child] <- x else c.Children.Add(x)
                        Grid.SetRow(x,i_row); Grid.SetColumn(x,i_col)
                        )
                    )
                |> fun x -> new CompositeDisposable(x)
                )
            |> d.Add
            obs.OnNext(c)
            d :> IDisposable
            )

    let vert_grid (l' : (Grid -> IDisposable) list) (l : (GridUnit * Control IObservable) list) =
        vert_grid' l' (l |> List.map (fun (s,row) -> s, [row]) |> Observable.ofSeq)

module Messaging =
    open System
    open System.Threading
    open System.Threading.Tasks
    open NetMQ
    open NetMQ.Sockets
    open System.Reactive.Disposables

    let run l =
        let l = l |> Array.map (fun f -> let poller = new NetMQPoller() in poller, Task.Run(fun () -> f poller : unit))
        Disposable.Create(fun () ->
            let pollers, tasks = Array.unzip l
            pollers |> Array.iter (fun (x : NetMQPoller) -> x.Stop())
            Task.WaitAll(tasks)
            (pollers, tasks) ||> Array.iter2 (fun a b -> a.Dispose(); b.Dispose())
            )

    module HelloWorld =
        let uri = "ipc://hello-world"
        let server (log : string -> unit) (poller: NetMQPoller) =
            try use server = new ResponseSocket()
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
                poller.Remove(server)
            with e -> log e.Message

        let client (log : string -> unit) (poller: NetMQPoller)  =
            try use client = new RequestSocket()
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
                    else 
                        poller.Stop()
                    )
                use __ = client.ReceiveReady.Subscribe(fun _ -> 
                    client.ReceiveFrameString() |> sprintf "Client received %s" |> log
                    )
                poller.Run()
                client.Disconnect(uri)
                poller.Remove(client)
            with e -> log e.Message

    module Weather =
        let uri = "ipc://weather"
        let server (log : string -> unit) (poller : NetMQPoller) =
            try let rand = Random()
                use pub = new PublisherSocket()
                poller.Add(pub)
                pub.Bind(uri)
                log "Publisher has bound."
                use __ = pub.SendReady.Subscribe(fun _ ->  
                    // get values that will fool the boss
                    let zipcode, temperature, relhumidity = rand.Next 100000, (rand.Next 215) - 80, (rand.Next 50) + 10
                    sprintf "%05d %d %d" zipcode temperature relhumidity |> pub.SendFrame
                    )
                poller.Run()
                pub.Unbind(uri)
                poller.Remove(pub)
            with e -> log e.Message

        let client (filter : string) (log : string -> unit) (poller : NetMQPoller) =
            try use sub = new SubscriberSocket()
                poller.Add(sub)
                sub.Connect(uri)
                sub.Subscribe(filter)
                log "Client has connected and subscribed."
                let i = ref 0
                let total_temp = ref 0
                use __ = sub.ReceiveReady.Subscribe(fun _ ->
                    if !i < 100 then
                        let update = sub.ReceiveFrameString()
                        let zipcode, temperature, relhumidity =
                            let update' = update.Split()
                            (int update'.[0]),(int update'.[1]),(int update'.[2])
                        total_temp := !total_temp + temperature
                        incr i
                        log (sprintf "Average temperature for zipcode '%s' is %dF" filter (!total_temp / !i))
                    else 
                        poller.Stop()
                    )
                poller.Run()
                sub.Unsubscribe(filter)
                sub.Disconnect(uri)
                poller.Remove(sub)
            with e -> log e.Message
    
module UI =
    open Lithe
    open Avalonia.Media
    open Avalonia.Controls
    open Avalonia.Layout

    open System.Reactive.Disposables
    open System.Reactive.Concurrency
    open System.Reactive.Subjects
    open FSharp.Control.Reactive

    let ui_scheduler = Avalonia.Threading.AvaloniaScheduler.Instance

    let text_list obs =
        border [
            do' <| fun x -> x.BorderBrush <- Brushes.Black; x.BorderThickness <- Thickness 0.5
            child <| scroll_viewer [
                //do' <| fun x -> x.MinHeight <- 500.0
                vert_grid' [cd_set [A 1.0; A 1.0]] (obs |> Observable.map(function 
                    | Some (i,x) ->
                        A 1.0, [
                            text_block [do' <| fun c -> c.Text <- sprintf "%i:" i; c.HorizontalAlignment <- HorizontalAlignment.Right]
                            text_block [do' <| fun c -> c.Text <- x]
                            ]
                    | None ->
                        A 1.0, [text_block [do' <| fun c -> c.Text <- "-----"; Grid.SetColumnSpan(c,2)]]
                    ))
                |> content
                prop (fun x _ -> x.Offset <- x.Offset.WithY(infinity)) obs
                ]
            ]

    type Msg = Add of id: int * msg: string
    type MsgStart = StartExample
    type State = Map<int,int>

    let tab_template l =
        let streams = Array.map (fun (name,_) -> name, Subject.Synchronize(Subject.broadcast,ui_scheduler)) l
        
        // The ThreadPoolScheduler assigns a different thread for each subscription and 
        // dispatches on them consistently for the lifetime of the subscription.
        let start_stream = Subject.Synchronize(Subject.broadcast,ThreadPoolScheduler.Instance)
        let state =
            start_stream 
            |> Observable.switchMap (fun StartExample -> 
                Observable.using (fun () -> 
                    // I changed to using an agent because on the weather example it is possible for messages to be sent before
                    // the Observable.scan is ready. This error is present in the 04_Zero_HelloWorld example.
                    let agent = FSharpx.Control.AutoCancelAgent.Start(fun mailbox -> async {
                        let line_counts = Array.zeroCreate l.Length
                        let rec loop () = async {
                            let! (Add(i,x)) = mailbox.Receive()
                            let count = line_counts.[i]
                            (snd streams.[i]).OnNext(Some(count, x))
                            line_counts.[i] <- count + 1
                            do! loop()
                            }
                        do! loop ()
                        })
                    l |> Array.mapi (fun i (_,f) -> f (fun x -> agent.Post(Add(i,x))))
                    |> Messaging.run
                    |> Disposable.compose (Disposable.Create(fun () -> streams |> Array.iter (fun (_,x) -> x.OnNext None)))
                    |> Disposable.compose agent
                    ) (fun _ -> Observable.neverWitness ())
                )
            |> Observable.publish
            |> Observable.refCount

        let start_button = A 1.0, button [
            do' <| fun x -> x.Content <- sprintf "Run %s" (streams |> Array.map fst |> String.concat " & ")
            event (fun x -> x.Click) (fun _ _ -> start_stream.OnNext StartExample)
            ]

        let stream_displays =
            streams |> Array.map (fun (name,stream) -> [
                A 1.0, text_block [do' <| fun x -> x.Text <- sprintf "%s:" name]
                P 230.0, text_list stream
                ])
            |> Array.toList |> List.concat
        
        scroll_viewer [
            content <| vert_grid [connect state] (start_button :: stream_displays)
            ]
    
    open Messaging
    let view = 
        window [
            do' <| fun t ->
                t.Height <- 600.0
                t.Width <- 700.0
                t.WindowStartupLocation <- WindowStartupLocation.CenterScreen
                t.Title <- "ZeroMQ Examples"
            content <| tab_control [
                items [
                    [do' (fun x -> x.Header <- "Hello World"); content (tab_template [|"Server", HelloWorld.server; "Client", HelloWorld.client|])]
                    [
                    do' (fun x -> x.Header <- "Weather") 
                    [|
                    "Server", Weather.server
                    "Client 1", Weather.client "10001"
                    "Client 2", Weather.client "10002"
                    "Client 3", Weather.client "10003"
                    "Client 4", Weather.client "10004"
                    "Client 5", Weather.client "10005"
                    "Client 6", Weather.client "10006"
                    "Client 7", Weather.client "10007"
                    |] |> tab_template |> content
                    ]
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
                let d = new Reactive.Disposables.SingleAssignmentDisposable()
                d.Disposable <- UI.view.Subscribe (fun v -> 
                    let v = v :?> Window
                    desktop.MainWindow <- v
                    v.Closing.Add(fun _ -> d.Dispose())
                    )
            | _ -> ()

            base.OnFrameworkInitializationCompleted()

    open Avalonia.Logging.Serilog
    [<CompiledName "BuildAvaloniaApp">] 
    let buildAvaloniaApp () = AppBuilder.Configure<App>().UsePlatformDetect().LogToDebug()

    let main argv = buildAvaloniaApp().StartWithClassicDesktopLifetime(argv)