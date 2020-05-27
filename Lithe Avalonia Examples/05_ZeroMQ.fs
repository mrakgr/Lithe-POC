module Avalonia.ZeroMQ

open System
open System.Threading

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

    let inline t a b x rest = try a x; rest() finally try b x with e -> ()
    module NetMQPoller =
        let inline add (poller : NetMQPoller) (socket : ISocketPollable) rest = (socket, rest) ||> t poller.Add poller.Remove
    module SubscriberSocket =
        let inline subscribe (socket : SubscriberSocket) (prefix : string) rest = (prefix, rest) ||> t socket.Subscribe socket.Unsubscribe
    module NetMQSocket =
        let inline bind uri (socket : NetMQSocket) rest = t socket.Bind socket.Unbind uri rest
        let inline connect uri (socket : NetMQSocket) rest = t socket.Connect socket.Disconnect uri rest
        let inline init (socket_create : unit -> #NetMQSocket) (poller : NetMQPoller) (connector : #NetMQSocket -> (unit -> 'r) -> 'r) rest =
            use socket = socket_create()
            NetMQPoller.add poller socket <| fun () ->
            connector socket <| fun () -> 
            rest socket
    
    open NetMQSocket

    module HelloWorld =
        let uri = "ipc://hello-world"
        let server (log : string -> unit) (poller: NetMQPoller) =
            try init ResponseSocket poller (bind uri) <| fun server ->
                log <| sprintf "Server has bound to: %s" uri
                use __ = server.ReceiveReady.Subscribe(fun x ->
                    let x = server.ReceiveFrameString()
                    log (sprintf "Server received %s" x)
                    Thread.Sleep(1000)
                    let msg = sprintf "%s World" x
                    log (sprintf "Server sending %s" msg)
                    server.SendFrame(msg)
                    )
                poller.Run()
            with e -> log e.Message

        let client (log : string -> unit) (poller: NetMQPoller)  =
            try init RequestSocket poller (connect uri) <| fun client ->
                log <| sprintf "Client has connected to: %s" uri
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
            with e -> log e.Message

    module Weather =
        let uri = "ipc://weather"
        let server (log : string -> unit) (poller : NetMQPoller) =
            try let rand = Random()
                init PublisherSocket poller (bind uri) <| fun pub ->
                log <| sprintf "Publisher has bound to %s." uri
                use __ = pub.SendReady.Subscribe(fun _ ->  
                    // get values that will fool the boss
                    let zipcode, temperature, relhumidity = rand.Next 100000, (rand.Next 215) - 80, (rand.Next 50) + 10
                    sprintf "%05d %d %d" zipcode temperature relhumidity |> pub.SendFrame
                    )
                poller.Run()
            with e -> log e.Message

        let client' uri (filter : string) (log : string -> unit) (poller : NetMQPoller) =
            try init SubscriberSocket poller (connect uri) <| fun sub ->
                SubscriberSocket.subscribe sub filter <| fun _ ->
                log <| sprintf "Client has connected to %s and subscribed to the topic %s." uri filter
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
            with e -> log e.Message

        let client = client' uri

    module DivideAndConquer =
        let task_number = 100
        let uri_sender, uri_sink = 
            let uri = "ipc://divide_and_conquer"
            IO.Path.Join(uri,"sender"), IO.Path.Join(uri,"sink")

        let ventilator timeout (log : string -> unit) (poller : NetMQPoller) =
            try let rnd = Random()
                init PushSocket poller (bind uri_sender) <| fun sender ->
                init PushSocket poller (connect uri_sink) <| fun sink ->
                let tasks = Array.init task_number (fun _ -> rnd.Next 100+1)
                log <| sprintf "Waiting %ims for the workers to get ready..." timeout
                Thread.Sleep(timeout)
                log <| sprintf "Running - total expected time: %A" (TimeSpan.FromMilliseconds(Array.sum tasks |> float))
                sink.SendFrame(string task_number)
                log <| "Sending tasks to workers."
                Array.iter (string >> sender.SendFrame) tasks
                log "Done sending tasks."
            with e -> log e.Message

        let worker (log : string -> unit) (poller : NetMQPoller) =
            try init PullSocket poller (connect uri_sender) <| fun sender ->
                init PushSocket poller (connect uri_sink) <| fun sink ->
                use __ = sender.ReceiveReady.Subscribe(fun _ ->
                    let msg = sender.ReceiveFrameString()
                    log <| sprintf "Received message %s." msg
                    Thread.Sleep(int msg)
                    sink.SendFrame("")
                    )
                poller.Run()
            with e -> log e.Message

        let sink (log : string -> unit) (poller : NetMQPoller) =
            try init PullSocket poller (bind uri_sink) <| fun sink ->
                let watch = Diagnostics.Stopwatch()
                use __ = sink.ReceiveReady.Subscribe(fun _ ->
                    let _ = sink.ReceiveFrameString()
                    log <| sprintf "Received message. Time elapsed: %A." watch.Elapsed
                    if watch.IsRunning = false then watch.Start()
                    )
                poller.Run()
            with e -> log e.Message

    module RequestAndReply =
        let request_number = 10
        let uri = "ipc://request_reply"
        let uri_worker, uri_client = IO.Path.Join(uri,"worker"), IO.Path.Join(uri,"client")

        let client (log : string -> unit) (poller : NetMQPoller) =
            try init RequestSocket poller (connect uri_client) <| fun requester ->
                log <| sprintf "Client has connected to %s" uri_client
                for i=1 to request_number do
                    requester.SendFrame("Hello")
                    let message = requester.ReceiveFrameString()
                    log <| sprintf "Received reply %i: %s" i message
            with e -> log e.Message

        let worker (log : string -> unit) (poller : NetMQPoller) =
            try init ResponseSocket poller (connect uri_worker) <| fun responder ->
                log <| sprintf "Worker has connected to %s" uri_worker
                use __ = responder.ReceiveReady.Subscribe(fun _ ->
                    let message = responder.ReceiveFrameString()
                    log <| sprintf "Received request: %s" message
                    Thread.Sleep(1)
                    responder.SendFrame("World")
                    )
                poller.Run()
            with e -> log e.Message

        let broker (log : string -> unit) (poller : NetMQPoller) =
            try init RouterSocket poller (bind uri_client) <| fun frontend ->
                log <| sprintf "Broker frontend has bound to %s" uri_client
                init DealerSocket poller (bind uri_worker) <| fun backend ->
                log <| sprintf "Broker backend has bound to %s" uri_worker
                Proxy(frontend,backend,null,poller).Start()
                // These two can be used instead of the proxy.
                //use __ = frontend.ReceiveReady.Subscribe(fun _ -> frontend.ReceiveMultipartMessage() |> backend.SendMultipartMessage)
                //use __ = backend.ReceiveReady.Subscribe(fun _ -> backend.ReceiveMultipartMessage() |> frontend.SendMultipartMessage)
                poller.Run()
            with e -> log e.Message

    module WeatherProxy =
        let uri_sub = "ipc://weather_internal"
        let broker (log : string -> unit) (poller : NetMQPoller) =
            try init XSubscriberSocket poller (connect Weather.uri) <| fun frontend ->
                log <| sprintf "Weather proxy frontend has connected to %s" Weather.uri
                init XPublisherSocket poller (bind uri_sub) <| fun backend ->
                log <| sprintf "Weather proxy backend has bound to %s" uri_sub
                Proxy(frontend,backend,null,poller).Start()
                poller.Run()
            with e -> log e.Message

        let client = Weather.client' uri_sub

    module KillSignal =
        let task_number = 100
        let uri_sender, uri_sink, uri_kill, uri_sink_start = 
            let uri = "ipc://kill_signaling"
            IO.Path.Join(uri,"sender"), IO.Path.Join(uri,"sink"), IO.Path.Join(uri,"kill"), IO.Path.Join(uri,"sink_start")

        let ventilator timeout (log : string -> unit) (poller : NetMQPoller) =
            try let rnd = Random()
                init PushSocket poller (bind uri_sender) <| fun sender ->
                init RequestSocket poller (connect uri_sink_start) <| fun sink ->
                let tasks = Array.init task_number (fun _ -> rnd.Next 100+1)
                log <| sprintf "Waiting %ims for the workers to get ready..." timeout
                Thread.Sleep(timeout)
                log <| sprintf "Running - total expected time: %A" (TimeSpan.FromMilliseconds(Array.sum tasks |> float))
                log "Starting the sink."
                sink.SendFrame(string task_number)
                sink.ReceiveMultipartMessage() |> ignore
                log "Sending tasks to workers."
                Array.iter (string >> sender.SendFrame) tasks
                log "Done sending tasks."
            with e -> log e.Message

        let worker (log : string -> unit) (poller : NetMQPoller) =
            try init PullSocket poller (connect uri_sender) <| fun sender ->
                init PushSocket poller (connect uri_sink) <| fun sink ->
                init SubscriberSocket poller (connect uri_kill) <| fun controller ->
                SubscriberSocket.subscribe controller "" <| fun _ ->
                use __ = controller.ReceiveReady.Subscribe(fun _ ->
                    let _ = controller.ReceiveMultipartMessage()
                    log "Received kill signal. Stopping."
                    poller.Stop()
                    )
                use __ = sender.ReceiveReady.Subscribe(fun _ ->
                    let msg = sender.ReceiveFrameString()
                    log <| sprintf "Received message %s." msg
                    Thread.Sleep(int msg)
                    sink.SendFrame("")
                    )
                poller.Run()
            with e -> log e.Message

        let sink (log : string -> unit) (poller : NetMQPoller) =
            try init PullSocket poller (bind uri_sink) <| fun sink ->
                init ResponseSocket poller (bind uri_sink_start) <| fun sink_start ->
                let near_to = sink_start.ReceiveFrameString() |> int
                sink_start.SendFrameEmpty()
                if near_to <= 0 then log "No tasks to process."
                else 
                    log <| sprintf "The number of tasks to process is %i" near_to
                    init PublisherSocket poller (bind uri_kill) <| fun controller ->
                    
                    let watch = Diagnostics.Stopwatch.StartNew()
                    let from = ref 0
                    let rest _ =
                        let _ = sink.ReceiveFrameString()
                        log <| sprintf "Received message. Time elapsed: %A." watch.Elapsed
                        incr from
                        if !from = near_to then controller.SendFrameEmpty(); log "Done with the tasks."; poller.Stop()
                    
                    use __ = sink.ReceiveReady.Subscribe(fun x -> rest x)
                    poller.Run()
            with e -> log e.Message

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
        let start_stream = Subject.Synchronize(Subject.broadcast,ui_scheduler) // TODO: Revert to the ThreadPool scheduler after resolving the Divide & Conquer message dropping bug.
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

        let start_button = button [
            do' <| fun x -> 
                x.Content <- sprintf "Run %s" (streams |> Array.map fst |> String.concat " & ")
                DockPanel.SetDock(x,Dock.Top)
            event (fun x -> x.Click) (fun _ _ -> start_stream.OnNext StartExample)
            ]

        let stream_displays =
            streams |> Array.map (fun (name,stream) -> [
                A 1.0, text_block [do' <| fun x -> x.Text <- sprintf "%s:" name]
                P 150.0, text_list stream
                ])
            |> Array.toList |> List.concat
        
        dock_panel [
            connect state
            children <| [
                start_button
                scroll_viewer [
                    content <| vert_grid [] stream_displays
                    ]
                ]
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
                    let tab header l = 
                        [
                        do' (fun (x : TabItem) -> x.Header <- header)
                        l |> tab_template |> content
                        ]
                    tab "Hello World" [|
                        "Server", HelloWorld.server
                        "Client", HelloWorld.client
                        |]
                    tab "Weather" [|
                        "Server", Weather.server
                        "Client 1", Weather.client "10001"
                        "Client 2", Weather.client "10002"
                        "Client 3", Weather.client "10003"
                        "Client 4", Weather.client "10004"
                        "Client 5", Weather.client "10005"
                        "Client 6", Weather.client "10006"
                        "Client 7", Weather.client "10007"
                        |]
                    tab "Divide & Conquer" [|
                        "Ventilator", DivideAndConquer.ventilator 1000
                        "Worker 1", DivideAndConquer.worker
                        "Worker 2", DivideAndConquer.worker
                        "Worker 3", DivideAndConquer.worker
                        "Worker 4", DivideAndConquer.worker
                        "Sink", DivideAndConquer.sink
                        |]
                    tab "Request & Reply" [|
                        "Client", RequestAndReply.client
                        "Worker", RequestAndReply.worker
                        "Broker", RequestAndReply.broker
                        |]
                    tab "Weather Proxy" [|
                        "Server", Weather.server
                        "Broker", WeatherProxy.broker
                        "Client 1", WeatherProxy.client "10007"
                        "Client 2", WeatherProxy.client "10008"
                        "Client 3", WeatherProxy.client "10009"
                        |]
                    tab "Kill Signaling" [|
                        "Ventilator", KillSignal.ventilator 1000
                        "Worker 1", KillSignal.worker
                        "Worker 2", KillSignal.worker
                        "Worker 3", KillSignal.worker
                        "Worker 4", KillSignal.worker
                        "Sink", KillSignal.sink
                        |]
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