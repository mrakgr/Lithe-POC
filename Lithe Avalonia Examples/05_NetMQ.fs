﻿// 6/5/2020: Since I am done with this, let me put in a few words.
// Originally when I started this, I had the intention of making every example restartable.
// If you look at the first hello world example you will see that unlike in the book, it does
// polling. There are a few problems with this.

// When I was starting out on this, I actually did not know that unlike with .NET `Task`s
// it is possible to abort threads. Had I known that from the start, I'd have gone for a
// different design. Even though `Messaging.run` now uses threads rather than tasks, it has 
// not been redesigned to take advantage of the addition capability of using raw threads.
// Calling `abort` on a thread would have been a lot more reliable than calling stop on the 
// poller passed into the function.

// Another problem is with the library itself. It is easy to open a socket, it easy to close
// it, but doing that on repeat is very difficult. Specifically, the NetMQ library does all
// binds and connects asynchronously as well unbinds and disconnects. Bind, unbind and bind
// the socket to the same address and you will get runtime errors, synchronization problems
// and dropped messages even for supposedly reliable sockets. It worked on me on the first 
// example, so I thought it would do fine on the rest, but I was wrong. I opened several issues 
// on the NetMQ issues pages and most of them are restart related. So even though every tab has
// that big restart button at the top consider it broken. At first I endeavored to make all
// the examples restartable, but all the bugs I've run into made me deviate from that goal.

// Lastly, I had no idea that it was possible to do ZeroMQ style polling until the end. Had
// I known that from the start, I'd have used that to make the examples more idiomatic. Check
// out the `poll` and `poll'` extensions in the `NetMQPoller` module and their references for
// examples.

// Problems with the UI:
// The UI features a fairly inspired reactive design. Unfortunately, I had the bright idea of
// adding every line as two text block controls. My reasoning was that I did not want to constantly
// keep having to regenerate the string, but what I did not count on was that even a moderately large 
// amount of lines would grind the UI to halt. You can see that effect on examples that run for long.

// It would have been better to use a single control for text. As a benefit it would have allowed
// me to keep the UI itself a pure function of the state. Right now, it is being mutated directly.
// It was an interesting experiment in UI library design, but I made the wrong choice here.

// Also, had I known how much trouble the restarts were going to be, I'd have given every thread
// its own cancel button in the UI. I mentioned that the issues with restarts crop up because the 
// rebind is too fast. If the restarts were done by hand on individual threads, there would be 
// enough lag so that things work properly. It would have been better if the top level button was a 
// switch rather than doing the restart immediately on click.

// What I wrote in the first section makes NetMQ look bad, but ironically these kinds of 
// issues would only crop up on toy examples like these. Using NetMQ in the real world, you would not 
// restart the servers while the process is still running and so would never run into them.

// Conclusion:
// I've done the examples from the first third of the book up to paranoid pirate (which is unfinished).
// Beyond that point, they get pretty complicated and are overkill for what I want to use NetMQ for so 
// I do not have the time nor the interest to pursue them anymore. If anybody is interested in learning 
// both ZeroMQ and UIs, it would be a good exercise to pick up where I left off by redesigning everything 
// in this file so that all the criticisms in this text are addressed.

// As for me, I'll get to work on editor support for Spiral starting with getting messaging to work between
// Node and .NET. I've been looking forward to that since February and I finally have the skills and the 
// tool to do it.

module Avalonia.NetMQ

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
        let l = l |> Array.map (fun f -> 
            let poller = new NetMQPoller()
            let thread = Thread(ThreadStart(fun () -> f poller),IsBackground=true)
            thread.Start()
            poller, thread)
        Disposable.Create(fun () ->
            l |> Array.iter (fun (poller,thread) -> poller.Stop())
            l |> Array.iter (fun (poller,thread) -> thread.Join(); poller.Dispose())
            )

    let inline t a b x rest = try a x; rest() finally try b x with e -> printfn "%s" e.Message
    module NetMQPoller =
        let inline add (poller : NetMQPoller) (socket : ISocketPollable) rest = (socket, rest) ||> t poller.Add poller.Remove
        let inline add_timer (poller : NetMQPoller) (socket : NetMQTimer) rest = (socket, rest) ||> t poller.Add poller.Remove

        let poll' (items : (NetMQSelector.Item * (NetMQSocket -> unit)) []) (t : TimeSpan) = 
            let b = NetMQSelector().Select(Array.map fst items,items.Length,t.Ticks)
            if b then items |> Array.iter (fun (s,f) -> if s.ResultEvent <> PollEvents.None then f s.Socket)
            b

        let poll items = poll' items (TimeSpan -1L)

    module NetMQSocket =
        let inline bind uri (socket : NetMQSocket) rest = t socket.Bind socket.Unbind uri rest
        let rec loop_connect (socket : NetMQSocket) x =
            try socket.Connect(x) 
            with :? EndpointNotFoundException -> Thread.Sleep(20); loop_connect socket x
        let inline connect uri (socket : NetMQSocket) rest = t (loop_connect socket) socket.Disconnect uri rest
        let inline connect' uri (socket : NetMQSocket) rest = t (List.iter (loop_connect socket)) (List.rev >> List.iter socket.Disconnect) uri rest
        let inline init (socket_create : unit -> #NetMQSocket) (poller : NetMQPoller) (connector : #NetMQSocket -> (unit -> 'r) -> 'r) rest =
            use socket = socket_create()
            NetMQPoller.add poller socket <| fun () ->
            connector socket <| fun () -> 
            rest socket

    open NetMQSocket

    module SubscriberSocket =
        let inline subscribe (socket : SubscriberSocket) (prefix : string) rest = (prefix, rest) ||> t socket.Subscribe socket.Unsubscribe

    module ResponseSocket =
        let inline sync_receive_string uri =
            use s = new ResponseSocket()
            bind uri s <| fun () ->
            let r = s.ReceiveFrameString()
            s.SendFrameEmpty()
            r

        // Note: This is broken as of 5/29/2020 since NetMQ is not diposing the sockets properly.
        let inline sync_receives uri num =
            use s = new ResponseSocket()
            bind uri s <| fun () ->
            for i=1 to num do
                let _ = s.ReceiveMultipartMessage()
                s.SendFrameEmpty()
            

    module RequestSocket =
        let inline sync_send_string uri x =
            use s = new RequestSocket()
            connect uri s <| fun () ->
            s.SendFrame(x : string)
            s.ReceiveFrameBytes() |> ignore
    
    module PairSocket =
        let receive_string uri rest =
            use receiver = new PairSocket()
            bind uri receiver <| fun () ->
            rest()
            receiver.ReceiveFrameString()

        let send_string uri (x : string) = 
            use receiver = new PairSocket()
            connect uri receiver <| fun () ->
            receiver.SendFrame(x)

    module NetMQMessage =
        let show (msg : NetMQMessage) = Seq.toArray msg |> Array.map (fun x -> if x.IsEmpty then "<null>" else x.ConvertToString()) |> String.concat " | "
        let equals (a : NetMQMessage) (b : NetMQMessage) = Seq.forall2 (fun (a : NetMQFrame) (b : NetMQFrame) -> a.Buffer = b.Buffer) a b
        /// Does a shallow copy of the message.
        let copy (msg : NetMQMessage) =
            let x = NetMQMessage(msg.FrameCount)
            for d in msg do x.Append(d)
            x

    module HelloWorld =
        let msg_num = 3
        let timeout = 1000
        let uri = "ipc://hello-world"
        let server (log : string -> unit) (poller: NetMQPoller) =
            try init ResponseSocket poller (bind uri) <| fun server ->
                log <| sprintf "Server has bound to: %s" uri
                
                use __ = server.ReceiveReady.Subscribe(fun x ->
                    let x = server.ReceiveFrameString()
                    log (sprintf "Server received %s" x)
                    Thread.Sleep(timeout)
                    let msg = sprintf "%s World" x
                    log (sprintf "Server sending %s" msg)
                    server.SendFrame(msg)
                    )
                poller.Run()
            with e -> log e.Message

        let client' uri (log : string -> unit) (poller: NetMQPoller)  =
            try init RequestSocket poller (connect uri) <| fun client ->
                log <| sprintf "Client has connected to: %s" uri
                let i = ref 0
                use __ = client.SendReady.Subscribe(fun _ -> 
                    if !i < msg_num then
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

        let client = client' uri

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
                        log (sprintf "Average temperature for zipcode '%s' since the start of the sequence is %dF" filter (!total_temp / !i))
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
                RequestSocket.sync_send_string uri_sink_start (string task_number)
                log "Ventilator has synced with the sink"
                let tasks = Array.init task_number (fun _ -> rnd.Next 100+1)
                log <| sprintf "Waiting %ims for the workers to get ready..." timeout
                Thread.Sleep(timeout)
                log <| sprintf "Running - total expected time: %A" (TimeSpan.FromMilliseconds(Array.sum tasks |> float))
                log "Starting the sink."
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
            try init PublisherSocket poller (bind uri_kill) <| fun controller ->
                init PullSocket poller (bind uri_sink) <| fun sink ->
                let near_to = ResponseSocket.sync_receive_string uri_sink_start |> int
                if near_to <= 0 then log "No tasks to process."
                else 
                    log <| sprintf "The number of tasks to process is %i" near_to
                    
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

    module MultithreadedService =
        let uri_clients = "ipc://multithreaded_service"
        let uri_workers = "inproc://workers"
        let thread_nbr = 4

        let worker (log : string -> unit) (poller : NetMQPoller) =
            init ResponseSocket poller (connect uri_workers) <| fun workers ->
            use __ = workers.ReceiveReady.Subscribe(fun _ ->
                workers.ReceiveFrameString() |> sprintf "Received request: %s" |> log 
                Thread.Sleep(1)
                workers.SendFrame("World")
                )
            log "Ready."
            poller.Run()

        let server (log : string -> unit) (poller : NetMQPoller) =
            try init RouterSocket poller (bind uri_clients) <| fun clients ->
                log <| sprintf "Router has bound to %s" uri_clients
                init DealerSocket poller (bind uri_workers) <| fun workers ->
                log <| sprintf "Dealer has bound to %s" uri_workers
                use __ = Array.init thread_nbr (fun i -> worker (sprintf "Worker %i: %s" i >> log)) |> run
                Proxy(clients,workers,null,poller).Start()
                log "Proxy has started."
                poller.Run()
            with e -> log e.Message

        let client = HelloWorld.client' uri_clients

    module RelayRace =
        let uri = "inproc://relay_race"
        let num_steps = 4

        let start (log : string -> unit) (poller : NetMQPoller) =
            let rec loop uri_parent l = 
                match l with
                | [] -> PairSocket.send_string uri_parent "READY"
                | uri_child :: xs ->
                    PairSocket.receive_string uri_child (fun () -> Thread(ThreadStart(fun () -> loop uri_child xs)).Start())
                    |> PairSocket.send_string uri_parent
                
            let uri_step = List.init num_steps (fun i -> IO.Path.Join(uri,sprintf "step%i" (i+1)))
            log <| sprintf "Starting the relay. The number of steps is %i" num_steps
            PairSocket.receive_string uri (fun () -> loop uri uri_step)
            |> sprintf "Received: %s" |> log

    module WeatherSynchronized =
        let uri = "ipc://weather_synchronized"
        let uri_start = IO.Path.Join(uri,"start")
        let num_subs = 10
        let num_messages = 1000000
        let msg_end = "END"
        let server (log : string -> unit) (poller : NetMQPoller) =
            try let rand = Random()
                init PublisherSocket poller (bind uri) <| fun pub ->
                log <| sprintf "Publisher has bound to %s." uri
                init ResponseSocket poller (bind uri_start) <| fun initer ->
                    for i=1 to num_subs do initer.ReceiveFrameString() |> ignore; initer.SendFrameEmpty()
                log "Publisher has synced."
                let i = ref 0
                use __ = pub.SendReady.Subscribe(fun _ ->  
                    // get values that will fool the boss
                    let zipcode, temperature, relhumidity = rand.Next 100000, (rand.Next 215) - 80, (rand.Next 50) + 10
                    sprintf "%05d %d %d" zipcode temperature relhumidity |> pub.SendFrame
                    incr i
                    if !i = num_messages then pub.SendFrame(msg_end); poller.Stop()
                    )
                poller.Run()
            with e -> log e.Message

        let client (filter : string) (log : string -> unit) (poller : NetMQPoller) =
            try init SubscriberSocket poller (connect uri) <| fun sub ->
                SubscriberSocket.subscribe sub filter <| fun _ ->
                SubscriberSocket.subscribe sub msg_end <| fun _ ->
                log <| sprintf "The client is also waiting for %s." msg_end
                log <| sprintf "Client has connected to %s and subscribed to the topic %s." uri filter
                RequestSocket.sync_send_string uri_start ""
                log "Synced with publisher."
                
                let i = ref 0
                let total_temp = ref 0
                use __ = sub.ReceiveReady.Subscribe(fun _ ->
                    let update = sub.ReceiveFrameString()
                    if update = msg_end then 
                        log <| sprintf "Got the %s message. Stopping." msg_end
                        poller.Stop()
                    else
                        let zipcode, temperature, relhumidity =
                            let update' = update.Split()
                            (int update'.[0]),(int update'.[1]),(int update'.[2])
                        total_temp := !total_temp + temperature
                        incr i
                        log (sprintf "Average temperature for zipcode '%s' since the start of the sequence is %dF" filter (!total_temp / !i))
                    )
                poller.Run()
            with e -> log e.Message

    module PubSubEnvelope =
        let uri = "ipc://pubsub_envelope"
        let uri_start = IO.Path.Join(uri,"start")
        let num_subs = 3
        let num_messages = 10
        let msg_end = "END"
        let server (log : string -> unit) (poller : NetMQPoller) =
            try init PublisherSocket poller (bind uri) <| fun pub ->
                log <| sprintf "Publisher has bound to %s." uri
                init ResponseSocket poller (bind uri_start) <| fun initer ->
                    for i=1 to num_subs do initer.ReceiveFrameString() |> ignore; initer.SendFrameEmpty()
                log "Publisher has synced."
                let i = ref 0
                use __ = pub.SendReady.Subscribe(fun _ ->  
                    let f l =
                        List.mapFoldBack (fun (x : string) s -> (x,s), true) l false
                        |> fst |> List.iter pub.SendFrame
                    f ["A"; "We don't want to see this"]
                    f ["B"; "We would like to see this"]
                    incr i
                    if !i = num_messages then log "Done sending messages"; pub.SendFrame(msg_end); poller.Stop()
                    )
                poller.Run()
            with e -> log e.Message

        let client (filter : string) (log : string -> unit) (poller : NetMQPoller) =
            try init SubscriberSocket poller (connect uri) <| fun sub ->
                init RequestSocket poller (connect uri_start) <| fun initer ->
                    initer.SendFrameEmpty(); initer.ReceiveFrameString() |> ignore
                log "Synced with publisher."
                SubscriberSocket.subscribe sub filter <| fun _ ->
                log <| sprintf "Client has connected to %s and subscribed to the topic %s." uri filter
                SubscriberSocket.subscribe sub msg_end <| fun _ ->
                log <| sprintf "The client is also waiting for %s." msg_end

                use __ = sub.ReceiveReady.Subscribe(fun _ ->
                    let msg = sub.ReceiveMultipartStrings()
                    if msg.[0] = msg_end then
                        log <| sprintf "Got the %s message. Stopping." msg_end
                        poller.Stop()
                    else
                        log <| sprintf "Got message: %s" msg.[1]
                    )
                poller.Run()
            with e -> log e.Message

    module IdentityCheck =
        let uri = "inproc://identity_check"
        let encoding = Text.Encoding.UTF8
        let main (log : string -> unit) (poller : NetMQPoller) =
            try init RouterSocket poller (bind uri) <| fun sink ->
                init RequestSocket poller (connect uri) <| fun anon ->
                anon.SendFrame("ROUTER uses a generated 5 byte identity")
                init RequestSocket poller (connect uri) <| fun ided ->
                ided.Options.Identity <- encoding.GetBytes("PEER2")
                ided.SendFrame("ROUTER socket uses REQ's socket identity")
                let a = sink.ReceiveMultipartMessage()
                log <| sprintf "Got: %s" (a.[2].ConvertToString())
                let b = sink.ReceiveMultipartMessage()
                log <| sprintf "Got: %s" (b.[2].ConvertToString(encoding))
            with e -> log e.Message

    module RouterReq =
        let time_run_in_seconds = 5
        let num_workers = 4
        let msg_fired = "Fired!"
        let uri = "ipc://router_req"
        let worker (log : string -> unit) _ =
            use req = new RequestSocket()
            connect uri req <| fun () ->
            log "Connected."
            let rnd = Random()
            let rec loop total =
                req.SendFrame("Hi Boss")
                let msg = req.ReceiveFrameString()
                if msg.ToString() = msg_fired then log <| sprintf "Completed %d tasks." total
                else Thread.Sleep(rnd.Next(1,500)); loop (total+1)
            loop 0

        open System.Reactive.Concurrency
        open FSharp.Control.Reactive
        let broker (log : string -> unit) _ =
            use broker = new RouterSocket()
            bind uri broker <| fun () ->
            use __ = Array.init num_workers (fun i -> worker (sprintf "Worker %i: %s" i >> log)) |> run

            let on_done workers_left next =
                broker.SendFrame(msg_fired)
                decr workers_left
                if !workers_left > 0 then next()
            let on_work next = broker.SendFrame("Worker harder."); next()

            let proxy = ref on_work 
            let rec loop () =
                let msg = broker.ReceiveMultipartMessage()
                broker.SendFrame(msg.[0].ToByteArray(),true)
                broker.SendFrameEmpty(true)
                !proxy loop

            use __ = 
                Observable.interval (TimeSpan.FromSeconds 1.0)
                |> Observable.take time_run_in_seconds
                |> Observable.subscribeWithCompletion 
                    (fun i -> let i = time_run_in_seconds - int i - 1 in if i > 0 then sprintf "%i..." i |> log)
                    (fun () -> log "Done."; proxy := on_done (ref num_workers))

            loop()

    module RouterDealer =
        let uri = "ipc://router_dealer"
        let msg_end = "END"
        let num_tasks = 100
        let worker (identity : string) (log : string -> unit) _ =
            use worker = new DealerSocket()
            worker.Options.Identity <- Text.Encoding.Default.GetBytes(identity)
            connect uri worker <| fun () ->
            log <| sprintf "Connected to %s" uri
            let rec loop count =
                let msg = worker.ReceiveMultipartMessage(2)
                if msg.[1].ConvertToString() = msg_end then count
                else loop (count + 1)
            loop 0 |> sprintf "Received total: %i" |> log

        type Worker = {|name : string; freq : int|}
        let client (workers : Worker list) (log : string -> unit) _ =
            use client = new RouterSocket()
            bind uri client <| fun () ->
            log <| sprintf "Bound to %s" uri
            Thread.Sleep(100) // 5/29/2020: Syncing using req/res sockets is even more broken than this after restarts.
            log "Done waiting."
            let send (name : string) (b : string) = client.SendFrame(name,true); client.SendFrameEmpty(true); client.SendFrame(b)
            let dist = workers |> List.map (fun x -> x.freq) |> List.scan (+) 0
            let near_to = List.last dist
            let rnd = Random()
            for _=1 to num_tasks do
                rnd.Next(near_to)
                |> fun to' -> List.findIndexBack (fun x -> x <= to') dist
                |> fun sample_i -> workers.[sample_i]
                |> fun x -> send x.name "This is the workload."
            for x in workers do send x.name msg_end
            log "Done."

    module RouterRouter =
        let uri_client = "ipc://load_balancing"
        let uri_worker = "inproc://load_balancing"
        let num_clients = 10
        let num_workers = 3
        let num_client_msgs = 3

        let client (log : string -> unit) (poller : NetMQPoller) =
            init RequestSocket poller (connect uri_client) <| fun client ->
            use __ = client.SendReady.Subscribe(fun _ ->
                let msg = "Hello"
                client.SendFrame(msg)
                sprintf "Sent: %s" msg |> log
                )
            let i = ref 0
            use __ = client.ReceiveReady.Subscribe(fun _ ->
                client.ReceiveFrameString() |> sprintf "Got: %s" |> log
                incr i
                if !i = num_client_msgs then poller.Stop()
                )
            poller.Run()
            log "Done."

        let msg_ready = "READY"
        let msg_ok = "OK"
        let worker (log : string -> unit) (poller : NetMQPoller) =
            Thread.Sleep(400)
            init RequestSocket poller (connect uri_worker) <| fun worker ->
            worker.SendFrame(msg_ready)
            sprintf "Sent: %s" msg_ready |> log
            use __ = worker.ReceiveReady.Subscribe(fun _ ->
                log "Ready to receive."
                let msg = worker.ReceiveMultipartMessage()
                let address = msg.Pop()
                msg.Pop() |> ignore
                msg.Pop().ConvertToString() |> sprintf "Got: %s" |> log
                msg.Append(address)
                msg.AppendEmptyFrame()
                msg.Append(msg_ok)
                worker.SendMultipartMessage(msg)
                )
            poller.Run()
            log "Done."

        let balancer (log : string -> unit) (poller : NetMQPoller) =
            init RouterSocket poller (bind uri_worker) <| fun backend ->
            log <| sprintf "The backend is bound to %s" uri_worker
            init RouterSocket poller (bind uri_client) <| fun frontend ->
            log <| sprintf "The frontend is bound to %s" uri_client
            
            let workers = System.Collections.Generic.Queue()

            let in' (s : NetMQSocket) f = NetMQSelector.Item(s,PollEvents.PollIn), f
            let items () = [|
                in' backend (fun _ ->
                    let msg = backend.ReceiveMultipartMessage()
                    let address = msg.Pop()
                    msg.Pop() |> ignore
                    if msg.FrameCount > 1 then frontend.SendMultipartMessage(msg)
                    workers.Enqueue address
                    )
                if workers.Count > 0 then
                    in' frontend (fun _ ->
                        let msg = frontend.ReceiveMultipartMessage()
                        msg.PushEmptyFrame()
                        msg.Push(workers.Dequeue())
                        backend.SendMultipartMessage(msg)
                        )
                |]
            while NetMQPoller.poll (items ()) do ()

    module AsyncServer =
        let uri_client = "ipc://random_server"
        let uri_worker = "inproc://random_server"
        let num_clients = 3
        let num_workers = 3
        let client (log : string -> unit) (poller : NetMQPoller) =
            Thread.Sleep(400)
            init DealerSocket poller (connect uri_client) <| fun client ->
            log <| sprintf "Client has connected to %s" uri_client
            let timer = NetMQTimer(1000)
            NetMQPoller.add_timer poller timer <| fun () ->
            let i = ref 0
            use __ = timer.Elapsed.Subscribe(fun _ ->
                incr i
                let msg = sprintf "request %i" !i
                client.SendFrame(msg)
                msg |> sprintf "Sent: %s" |> log
                )
            use __ = client.ReceiveReady.Subscribe(fun _ ->
                client.ReceiveFrameString() |> sprintf "Got: %s" |> log
                )
            poller.Run()

        let worker (log : string -> unit) (poller : NetMQPoller) =
            Thread.Sleep(400)
            init DealerSocket poller (connect uri_worker) <| fun worker ->
            log <| sprintf "Worker has connected to %s" uri_worker
            let rng = Random()
            use __ = worker.ReceiveReady.Subscribe(fun _ ->
                let msg = worker.ReceiveMultipartMessage()
                let replies = rng.Next(5)
                for i=0 to replies do
                    Thread.Sleep(rng.Next(750))
                    worker.SendMultipartMessage(msg)
                )
            poller.Run()

        let server (log : string -> unit) (poller : NetMQPoller) =
            init RouterSocket poller (bind uri_client) <| fun frontend ->
            log <| sprintf "Frontend has bound to %s" uri_client
            init DealerSocket poller (bind uri_worker) <| fun backend ->
            log <| sprintf "Backend has bound to %s" uri_worker
            Proxy(frontend,backend,null,poller).Start()
            poller.Run()

    module PeeringStatePrototype =
        let uri_workers_avail name = sprintf "ipc://peering_state_prototype/%s/workers_avail" name
        let state name_self name_rest (log : string -> unit) (poller : NetMQPoller) =
            let uri_self = uri_workers_avail name_self
            let uri_rest = List.map uri_workers_avail name_rest
            init PublisherSocket poller (bind uri_self) <| fun statebe ->
            log <| sprintf "%s has bound to %s" name_self uri_self
            init SubscriberSocket poller (connect' uri_rest) <| fun statefe ->
            log <| sprintf "%s has connected to %s" name_self (String.concat " & " uri_rest)
            statefe.SubscribeToAnyTopic()
            let timer = NetMQTimer(1000)
            NetMQPoller.add_timer poller timer <| fun () ->
            let rnd = Random()
            use __ = timer.Elapsed.Subscribe(fun _ ->
                let msg = NetMQMessage()
                msg.Append(uri_self)
                msg.Append(rnd.Next(10))
                statebe.SendMultipartMessage(msg)
                )
            use __ = statefe.ReceiveReady.Subscribe(fun _ ->
                let msg = statefe.ReceiveMultipartMessage(2)
                let uri = msg.[0].ConvertToString()
                let workers_avail = msg.[1].ConvertToInt32()
                log <| sprintf "%s has %i workers free." uri workers_avail
                )
            poller.Run()

    module PeeringLocalCloudPrototype =
        let uri name = sprintf "ipc://peering_local_cloud_prototype/%s" name
        let join l = IO.Path.Join(l |> List.toArray)
        let client' = "client"
        let worker' = "worker"
        let cloud = "cloud"
        let num_clients = 10
        let num_workers = 3
        let num_client_msgs = 5

        let client name (log : string -> unit) (poller : NetMQPoller) =
            let uri_client = join [uri name; client']
            init RequestSocket poller (connect uri_client) <| fun client ->
            for i=1 to num_client_msgs do
                let msg = "Hello"
                client.SendFrame(msg)
                sprintf "Sent: %s" msg |> log
                client.ReceiveFrameString() |> sprintf "Got: %s" |> log
            log "Done."

        let msg_ready = "READY"
        let msg_ok = "World"
        let worker name (log : string -> unit) (poller : NetMQPoller) =
            let uri_worker = join [uri name; worker']
            init RequestSocket poller (connect uri_worker) <| fun worker ->
            worker.SendFrame(msg_ready)
            sprintf "Sent: %s" msg_ready |> log
            use __ = worker.ReceiveReady.Subscribe(fun _ ->
                let msg = worker.ReceiveMultipartMessage()
                let ret = sprintf "%s %s" (msg.Last.ConvertToString()) msg_ok
                msg.RemoveFrame(msg.Last) |> ignore
                msg.Append(ret)
                worker.SendMultipartMessage(msg)
                )
            poller.Run()
            log "Done."

        open System.Collections.Generic
        let balancer name_self name_rest (log : string -> unit) (poller : NetMQPoller) =
            let uri_self = uri name_self
            let uri' = join [uri_self; worker']
            init RouterSocket poller (bind uri') <| fun backend_local ->
            log <| sprintf "The local backend is bound to %s" uri'
            let uri' = List.map (fun name -> join [uri name; client'; cloud]) name_rest
            init RouterSocket poller (connect' uri') <| fun backend_cloud ->
            backend_cloud.Options.RouterMandatory <- true
            log <| sprintf "The cloud backend is connected to %s" (String.concat " & " uri')
            let uri' = join [uri_self; client']
            init RouterSocket poller (bind uri') <| fun frontend_local ->
            log <| sprintf "The local frontend is bound to %s" uri'
            let uri' = join [uri_self; client'; cloud]
            init RouterSocket poller (bind uri') <| fun frontend_cloud ->
            log <| sprintf "The cloud frontend is bound to %s" uri'

            let id_self = Text.Encoding.Default.GetBytes(name_self)
            // TODO: I thought to implement hop shortcutting on return, but return addresses get garbled even with the identity set
            // for some reason. Why is that? Do the return channels all have to be unique? Also why does `C` never show up in
            // the printouts?
            //backend_cloud.Options.Identity <- id_self // Does not work as intended.
            frontend_cloud.Options.Identity <- id_self // This is necessary. Destinations have to have fixed addresses.

            let id_rest = name_rest |> List.map Text.Encoding.Default.GetBytes
            // After trying out various other ways of doing it, this is the only pattern that I've found that actually works
            // with the way NetMQ does polling. Even though the guide assures me adding and removing sockets from the poller
            // is thread safe, the attempt to take advantage of that quickly ran into issues with the callbacks being triggered 
            // even for removed sockets.
            let workers = Queue()
            let clients = Queue()

            let queue_try_work () =
                if clients.Count > 0 && workers.Count > 0 then
                    let client : NetMQMessage = clients.Dequeue()
                    let worker : NetMQFrame = workers.Dequeue()
                    client.PushEmptyFrame()
                    client.Push(worker)
                    backend_local.SendMultipartMessage(client)

            let backend_handle (x : NetMQSocketEventArgs) =
                let msg = x.Socket.ReceiveMultipartMessage()
                let address = msg.Pop()
                msg.Pop() |> ignore
                match msg.FrameCount / 2 with
                | 0 -> () // ready
                | 1 -> frontend_local.SendMultipartMessage(msg) // local message
                | c -> frontend_cloud.SendMultipartMessage(msg) // routed message
                address

            use __ = backend_local.ReceiveReady.Subscribe(backend_handle >> workers.Enqueue >> queue_try_work)
            use __ = backend_cloud.ReceiveReady.Subscribe(backend_handle >> ignore)
            let rnd = Random()
            let frontend_handle (x : NetMQSocketEventArgs) = 
                let msg = x.Socket.ReceiveMultipartMessage()
                
                // This part is a bit different from the guide as it is possible for message to be rerouted an arbitrary number of times.
                if rnd.Next(5) = 0 then 
                    msg.PushEmptyFrame()
                    let r = id_rest.[rnd.Next(List.length id_rest)]
                    msg.Push(r)

                    let rec try_send msg =
                        try backend_cloud.SendMultipartMessage(msg)
                        with :? HostUnreachableException -> Thread.Sleep(500); try_send msg
                    try_send msg
                else
                    clients.Enqueue(msg)
                    queue_try_work()
            use __ = frontend_local.ReceiveReady.Subscribe frontend_handle
            use __ = frontend_cloud.ReceiveReady.Subscribe frontend_handle
            poller.Run()

    module PeeringFull =
        let uri name = sprintf "ipc://peering_full/%s" name
        let uri_workers_available = uri "workers_available"
        let uri_workers_available_push = uri "workers_available/push"
        let join l = IO.Path.Join(l |> List.toArray)
        let client' = "client"
        let worker' = "worker"
        let cloud = "cloud"
        let num_clients = 10
        let num_workers = 3
        let num_bursts = 5
        let num_client_msgs = 20
        let num_burst_timeout = 300
                
        let publisher (log : string -> unit) (poller : NetMQPoller) =
            init PullSocket poller (bind uri_workers_available_push) <| fun monitor ->
            log <| sprintf "Monitor has bound to: %s" uri_workers_available_push
            init PublisherSocket poller (bind uri_workers_available) <| fun pub ->
            log <| sprintf "Publisher has bound to: %s" uri_workers_available

            use __ = monitor.ReceiveReady.Subscribe(fun x -> x.Socket.ReceiveMultipartMessage(2) |> pub.SendMultipartMessage)
            poller.Run()

        let task_id = let i = ref 0 in fun () -> Interlocked.Add(i,1)
        let client name_cluster (log : string -> unit) (poller : NetMQPoller) =
            let uri_client = join [uri name_cluster; client']
            init RequestSocket poller (connect uri_client) <| fun client ->
            let rnd = Random()
            for _=1 to num_bursts do
                let to' = rnd.Next(num_client_msgs)+1
                for _=1 to to' do
                    let msg = task_id()
                    client.SendFrame(sprintf "task_id: %i" msg)
                    //sprintf "Sent task: %i" msg |> log
                    client.ReceiveFrameString() 
                    //|> sprintf "Got: %s" |> log
                    |> ignore
                Thread.Sleep(rnd.Next(num_burst_timeout))
            log "Done."

        let msg_ready = "READY"
        let worker name_cluster (log : string -> unit) (poller : NetMQPoller) =
            let uri_worker = join [uri name_cluster; worker']
            init RequestSocket poller (connect uri_worker) <| fun worker ->
            worker.SendFrame(msg_ready)
            sprintf "Sent: %s" msg_ready |> log
            let rnd = Random()
            use __ = worker.ReceiveReady.Subscribe(fun _ ->
                let msg = worker.ReceiveMultipartMessage()
                Thread.Sleep(rnd.Next(20))
                worker.SendMultipartMessage(msg)
                )
            poller.Run()
            log "Done."

        open System.Collections.Generic
        let balancer name_cluster (log : string -> unit) (poller : NetMQPoller) =
            let uri_self = uri name_cluster
            let uri' = join [uri_self; worker']
            init RouterSocket poller (bind uri') <| fun backend_local ->
            log <| sprintf "The local backend is bound to %s" uri'
            init RouterSocket poller (connect' []) <| fun backend_cloud ->
            log "The cloud backend has finished init."
            backend_cloud.Options.RouterMandatory <- true
            let uri' = join [uri_self; client']
            init RouterSocket poller (bind uri') <| fun frontend_local ->
            log <| sprintf "The local frontend is bound to %s" uri'
            let uri' = join [uri_self; client'; cloud]
            init RouterSocket poller (bind uri') <| fun frontend_cloud ->
            log <| sprintf "The cloud frontend is bound to %s" uri'
            init SubscriberSocket poller (connect uri_workers_available) <| fun workers_avail ->
            workers_avail.SubscribeToAnyTopic()
            log <| sprintf "Workers available has connected and subscribed to: %s" uri_workers_available
            init PushSocket poller (connect uri_workers_available_push) <| fun workers_avail_push ->
            log <| sprintf "Workers available push has connected: %s" uri_workers_available_push

            let id_self = Text.Encoding.Default.GetBytes(name_cluster)
            frontend_cloud.Options.Identity <- id_self

            let workers_cloud = Dictionary()
            let workers = Queue()
            let clients = Queue()

            let rnd = Random()
            let worker_cloud_sample on_succ =
                let dist = Seq.toArray workers_cloud
                let dist_cdf = dist |> Array.scan (fun s x -> s + x.Value) 0
                let max = Array.last dist_cdf
                if max > 0 then
                    let id = 
                        rnd.Next(max)
                        |> fun from -> Array.findIndex (fun near_to -> from < near_to) dist_cdf
                        |> fun i -> dist.[i-1].Key
                    on_succ id

            let workers_avail_switch =
                let mutable old = workers.Count
                fun () ->
                    let x = workers.Count
                    if old <> x then 
                        let msg = NetMQMessage()
                        msg.Append(name_cluster); msg.Append(x)
                        workers_avail_push.SendMultipartMessage(msg)
                        old <- x

            let queue_try_work () =
                if clients.Count > 0 then
                    if workers.Count > 0 then
                        let worker : NetMQFrame = workers.Dequeue()
                        let client : NetMQMessage = clients.Dequeue()
                        client.PushEmptyFrame()
                        client.Push(worker)
                        backend_local.SendMultipartMessage(client)
                    else
                        worker_cloud_sample <| fun (id : string) ->
                            log <| sprintf "Routing to %s" id
                            let msg = clients.Dequeue()
                            msg.PushEmptyFrame()
                            msg.Push(id)
                            let rec try_send msg = // NetMQ is annoying in how the connects are non-blocking. The solution is to just keep trying.
                                try backend_cloud.SendMultipartMessage msg
                                with :? HostUnreachableException -> Thread.Sleep(20); try_send msg
                            try_send msg
                workers_avail_switch()

            let backend_handle (x : NetMQSocketEventArgs) =
                let msg = x.Socket.ReceiveMultipartMessage()
                let address = msg.Pop()
                msg.Pop() |> ignore
                match msg.FrameCount / 2 with
                | 0 -> () // ready
                | 1 -> frontend_local.SendMultipartMessage(msg) // local message
                | c -> frontend_cloud.SendMultipartMessage(msg) // routed message
                address

            use __ = backend_local.ReceiveReady.Subscribe(backend_handle >> workers.Enqueue >> queue_try_work)
            use __ = backend_cloud.ReceiveReady.Subscribe(backend_handle >> ignore)
            
            let frontend_handle (x : NetMQSocketEventArgs) = 
                let msg = x.Socket.ReceiveMultipartMessage()
                clients.Enqueue(msg)
                queue_try_work()
            use __ = frontend_local.ReceiveReady.Subscribe frontend_handle
            use __ = frontend_cloud.ReceiveReady.Subscribe frontend_handle

            use __ = workers_avail.ReceiveReady.Subscribe(fun x ->
                let msg = x.Socket.ReceiveMultipartMessage()
                let name, num = msg.[0].ConvertToString(), msg.[1].ConvertToInt32()
                if name <> name_cluster then
                    if workers_cloud.ContainsKey name = false then
                        let r = join [uri name; client'; cloud]
                        backend_cloud.Connect(r)
                        log <| sprintf "Backend cloud has connected to: %s" r
                    workers_cloud.[name] <- num
                    queue_try_work()
                )
            poller.Run()

    module LazyPirate =
        let uri = "ipc://lazy_pirate"
        let task_id = let x = ref 0 in fun () -> Interlocked.Add(x,1)
        let timeout = TimeSpan.FromSeconds 0.5
        let num_requests = 20
        let num_retries = 6
        let client (log : string -> unit) (poller : NetMQPoller) =
            let rec loop_req i =
                let id = task_id()
                let rec loop_retry retries =
                    let is_succ =
                        init RequestSocket poller (connect uri) <| fun req ->
                        req.SendFrame(sprintf "task %i" id)
                        let mutable s = null
                        if req.TryReceiveFrameString(timeout,&s) then log <| sprintf "Received: %s" s; true
                        else log <| sprintf "Task %i timed out." id; false
                    if is_succ then loop_req (i+1)
                    elif retries > 0 then log "Retrying..."; loop_retry (retries-1)
                    else log "Aborting." 
                if i < num_requests then loop_retry num_retries
                else log "Done."
            loop_req 0
                
        let server (log : string -> unit) (poller : NetMQPoller) =
            init ResponseSocket poller (bind uri) <| fun res ->
            let rnd = Random()
            let rec loop i =
                let msg = res.ReceiveFrameString()
                let tired = 2 < i
                if tired && rnd.Next(8) = 0 then log "Simulating a crash." else
                if tired && rnd.Next(3) = 0 then log "Simulating an overload."; Thread.Sleep(timeout)
                Thread.Sleep(timeout/2.0)
                log <| sprintf "Got: %s" msg
                res.SendFrame(msg)
                loop (i+1)

            loop 0

    module SimplePirate =
        let uri_frontend = "ipc://simple_pirate"
        let uri_backend = "inproc://simple_pirate"

        let task_id = let x = ref 0 in fun () -> Interlocked.Add(x,1)
        let timeout = TimeSpan.FromSeconds 0.5
        let num_requests = 20
        let num_retries = 6
        let num_clients = 10
        let num_workers = 3
        let client (log : string -> unit) (poller : NetMQPoller) =
            Thread.Sleep(1000)
            let rnd = Random()
            let rec loop_req i =
                let id = task_id()
                let rec loop_retry retries =
                    let is_succ =
                        init DealerSocket poller (connect uri_frontend) <| fun req ->
                        req.SendFrame(sprintf "task %i" id)
                        // I am doing a little trick to make the send synchronous.
                        // I thought that the previous version of the balancer which used two queues was async due to
                        // the receives being done prematurely, but that turned out to be a false assumption.
                        if req.TrySkipMultipartMessage(timeout*3.0) then 
                            let mutable s = null
                            if req.TryReceiveFrameString(timeout,&s) then log <| sprintf "Received: %s" s; true
                            else log <| sprintf "Task %i's work timed out." id; false
                        else log <| sprintf "Task %i's send timed out." id; false
                    if is_succ then loop_req (i+1)
                    elif retries > 0 then log "Retrying..."; loop_retry (retries-1)
                    else log "Aborting." 
                if i < num_requests then loop_retry (num_retries + rnd.Next(num_retries))
                else log "Done."
            loop_req 0

        let worker (log : string -> unit) (poller : NetMQPoller) =
            init RequestSocket poller (connect uri_backend) <| fun res ->
            res.SendFrameEmpty()
            log "Ready"
            let rnd = Random()
            let rec loop i =
                let msg = res.ReceiveMultipartMessage()
                let tired = 2 < i
                //if tired && rnd.Next(8) = 0 then log "Simulating a crash." else
                if tired && rnd.Next(3) = 0 then log "Simulating an overload."; Thread.Sleep(timeout)
                Thread.Sleep(timeout/2.0)
                log <| sprintf "Got: %s" (msg.Last.ConvertToString())
                res.SendMultipartMessage(msg)
                loop (i+1)

            loop 0

        let balancer (log : string -> unit) (poller : NetMQPoller) =
            init RouterSocket poller (bind uri_frontend) <| fun frontend ->
            log <| sprintf "Frontend has connected to %s" uri_frontend
            init RouterSocket poller (bind uri_backend) <| fun backend ->
            log <| sprintf "Backend has connected to %s" uri_backend

            let workers = System.Collections.Generic.Queue()

            let in' (s : NetMQSocket) f = NetMQSelector.Item(s,PollEvents.PollIn), f
            let items () = [|
                in' backend (fun _ ->
                    let msg = backend.ReceiveMultipartMessage()
                    let address = msg.Pop()
                    msg.Pop() |> ignore
                    if msg.FrameCount > 1 then frontend.SendMultipartMessage(msg)
                    workers.Enqueue address
                    )
                if workers.Count > 0 then
                    in' frontend (fun _ ->
                        let msg = frontend.ReceiveMultipartMessage()
                        frontend.SendFrame(msg.First.Buffer,true); frontend.SendFrameEmpty()
                        msg.PushEmptyFrame()
                        msg.Push(workers.Dequeue())
                        backend.SendMultipartMessage(msg)
                        )
                |]
            while NetMQPoller.poll (items ()) do ()

    // This example is incomplete because it is a huge mess and I do not feel like doing it.
    // Rather than implementing heartbeating for workers, it makes more sense for the workers to restart themselves and reconnect.
    // Basically, it makes no sense to me in the context of how the example was structured.
    // Let me move to the next thing.
    module ParanoidPirate =
        open MBrace.FsPickler
        let bin = BinarySerializer()
        let uri_frontend = "ipc://paranoid_pirate"
        let uri_backend = "inproc://paranoid_pirate"

        let task_id = let x = ref 0 in fun () -> Interlocked.Add(x,1)
        let timeout = TimeSpan.FromSeconds 0.5
        let num_requests = 20
        let num_retries = 6
        let num_clients = 10
        let num_workers = 3
        let client (log : string -> unit) (poller : NetMQPoller) =
            Thread.Sleep(500)
            init DealerSocket poller (connect uri_frontend) <| fun req ->
            let rec loop_req i =
                if i < num_requests then 
                    let id = task_id()
                    let msg = sprintf "task %i" id
                    let rec loop_retry retries =
                        let is_succ =
                            req.SendFrame(msg)
                            let mutable s = null
                            let rec receive () =
                                if req.TryReceiveFrameString(timeout,&s) then 
                                    if s = msg then log <| sprintf "Received: %s" s; true
                                    else log <| sprintf "Received invalid: %s" s; receive()
                                else log <| sprintf "Task %i timed out." id; false
                            receive()
                        if is_succ then loop_req (i+1)
                        elif retries > 0 then log "Retrying..."; loop_retry (retries-1)
                        else log "Aborting." 
                    loop_retry num_retries
                else log "Done."
            loop_req 0

        type WorkerMsg =
            | Ready
            | ClientTaskDone of string * string

        type BalancerToWorkerMsg =
            | Restart
            | ClientTask of string

        type WorkerState =
            | Running
            | Crashed

        let worker (log : string -> unit) (poller : NetMQPoller) =
            init RequestSocket poller (connect uri_backend) <| fun res ->
            res.SendFrameEmpty()
            log "Ready"
            let rnd = Random()
            let rec loop i =
                let msg = res.ReceiveMultipartMessage()
                let tired = 2 < i
                //if tired && rnd.Next(8) = 0 then log "Simulating a crash." else
                if tired && rnd.Next(3) = 0 then log "Simulating an overload."; Thread.Sleep(timeout)
                Thread.Sleep(timeout/2.0)
                log <| sprintf "Got: %s" (msg.Last.ConvertToString())
                res.SendMultipartMessage(msg)
                loop (i+1)

            loop 0

        let balancer (log : string -> unit) (poller : NetMQPoller) =
            init RouterSocket poller (bind uri_frontend) <| fun frontend ->
            log <| sprintf "Frontend has connected to %s" uri_frontend
            init RouterSocket poller (bind uri_backend) <| fun backend ->
            log <| sprintf "Backend has connected to %s" uri_backend

            let users_prev_requests = System.Collections.Generic.Dictionary()
            let workers_time = System.Collections.Generic.Dictionary()
            let clients = System.Collections.Generic.Queue()
            let workers = System.Collections.Generic.Queue()

            let in' (s : NetMQSocket) f = NetMQSelector.Item(s,PollEvents.PollIn), f
            let items () = [|
                in' backend (fun _ ->
                    let msg = backend.ReceiveMultipartMessage()
                    let address = msg.Pop()
                    msg.Pop() |> ignore
                    if msg.FrameCount > 1 then frontend.SendMultipartMessage(msg)
                    workers.Enqueue address
                    )
                if workers.Count > 0 then
                    in' frontend (fun _ ->
                        let msg = frontend.ReceiveMultipartMessage()
                        let address = msg.Pop()
                        match users_prev_requests.TryGetValue(address) with
                        | true, v when NetMQMessage.equals msg v -> ()
                        | _ ->
                            users_prev_requests.[address] <- NetMQMessage.copy msg
                            msg.Push(address)
                            msg.PushEmptyFrame()
                            msg.Push(workers.Dequeue())
                            backend.SendMultipartMessage(msg)
                        )
                |]
            while NetMQPoller.poll (items ()) do ()


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
                content <| vert_grid' [cd_set [A 1.0; A 1.0]] (obs |> Observable.map(function 
                    | Some (i,x) ->
                        A 1.0, [
                            text_block [do' <| fun c -> c.Text <- sprintf "%i:" i; c.HorizontalAlignment <- HorizontalAlignment.Right]
                            text_block [do' <| fun c -> c.Text <- x]
                            ]
                    | None ->
                        A 1.0, [text_block [do' <| fun c -> c.Text <- "-----"; Grid.SetColumnSpan(c,2)]]
                    ))
                prop (fun x _ -> x.Offset <- x.Offset.WithY(infinity)) (obs |> Observable.delayOn ui_scheduler (TimeSpan.FromSeconds 0.01))
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
                    tab "Multithreaded Service" [|
                        "Server", MultithreadedService.server
                        "Client 1", MultithreadedService.client
                        "Client 2", MultithreadedService.client
                        "Client 3", MultithreadedService.client
                        "Client 4", MultithreadedService.client
                        |]
                    tab "Relay Race" [|
                        "Relay", RelayRace.start
                        |]
                    tab "Weather Sync" (
                        Array.append [|"Server", WeatherSynchronized.server|] <| Array.init WeatherSynchronized.num_subs (fun i -> 
                            let i=i+1 in sprintf "Client %i" i, WeatherSynchronized.client (sprintf "%05d" (10000+i))
                            )
                        )
                    tab "PubSub Envelope" (
                        Array.append [|"Server", PubSubEnvelope.server|] <| Array.init PubSubEnvelope.num_subs (fun i -> 
                            sprintf "Client %i" (i+1), PubSubEnvelope.client "B"
                            )
                        )
                    tab "Identity Check" [|
                        "Main", IdentityCheck.main
                        |]
                    tab "Router-Req" [|
                        "Broker", RouterReq.broker
                        |]
                    tab "Router-Dealer" (
                        let workers = [{|name="A"; freq=2|}; {|name="B"; freq=1|}]
                        let client = "Broker", RouterDealer.client workers
                        let workers = workers |> List.map (fun x -> sprintf "Worker %s" x.name, RouterDealer.worker x.name)
                        client :: workers |> List.toArray
                        )
                    tab "Router-Router" (
                        let balancer = "Balancer", RouterRouter.balancer
                        let clients = List.init RouterRouter.num_clients (fun i -> sprintf "Client %i" i, RouterRouter.client)
                        let workers = List.init RouterRouter.num_workers (fun i -> sprintf "Worker %i" i, RouterRouter.worker)
                        balancer :: clients @ workers |> List.toArray
                        )
                    tab "Async Server" (
                        let server = "Server", AsyncServer.server
                        let clients = List.init AsyncServer.num_clients (fun i -> sprintf "Client %i" i, AsyncServer.client)
                        let workers = List.init AsyncServer.num_workers (fun i -> sprintf "Worker %i" i, AsyncServer.worker)
                        server :: clients @ workers |> List.toArray
                        )
                    tab "Peering State Prototype" (
                        Array.unfold(function
                            | (x :: xs as l, c) when c > 0 -> Some(l, (xs @ [x], c-1))
                            | _ -> None) (let l = ["A"; "B"; "C"] in l, List.length l)
                        |> Array.map (function (name :: rest) -> sprintf "Pub %s" name, PeeringStatePrototype.state name rest | _ -> failwith "impossible")
                        )
                    tab "Peering Local Cloud Prototype" (
                        Array.unfold(function
                            | (x :: xs as l, c) when c > 0 -> Some(l, (xs @ [x], c-1))
                            | _ -> None) (let l = ["A"; "B"; "C"] in l, List.length l)
                        |> Array.collect (function 
                            | (name :: rest) -> 
                                let balancer = sprintf "Balancer %s" name, PeeringLocalCloudPrototype.balancer name rest
                                let clients = List.init PeeringLocalCloudPrototype.num_clients (fun i -> sprintf "Client %s-%i" name i, PeeringLocalCloudPrototype.client name)
                                let workers = List.init PeeringLocalCloudPrototype.num_workers (fun i -> sprintf "Worker %s-%i" name i, PeeringLocalCloudPrototype.worker name)
                                balancer :: clients @ workers |> List.toArray
                            | _ -> failwith "impossible"
                            )
                        )
                    tab "Peering Full" (
                        ["A"; "B"; "C"]
                        |> List.collect (fun name ->
                            let balancer = sprintf "Balancer %s" name, PeeringFull.balancer name
                            let clients = List.init PeeringFull.num_clients (fun i -> sprintf "Client %s-%i" name i, PeeringFull.client name)
                            let workers = List.init PeeringFull.num_workers (fun i -> sprintf "Worker %s-%i" name i, PeeringFull.worker name)
                            balancer :: clients @ workers
                            )
                        |> fun l -> ("Publisher", PeeringFull.publisher) :: l 
                        |> List.toArray
                        )
                    tab "Lazy Pirate" [|
                        "Client", LazyPirate.client
                        "Server", LazyPirate.server
                        |]
                    tab "Simple Pirate" (
                        let balancer = sprintf "Balancer", SimplePirate.balancer
                        let clients = List.init SimplePirate.num_clients (fun i -> sprintf "Client %i" i, SimplePirate.client)
                        let workers = List.init SimplePirate.num_workers (fun i -> sprintf "Worker %i" i, SimplePirate.worker)
                        balancer :: clients @ workers |> List.toArray
                        )
                    //tab "Paranoid Pirate" (
                    //    let balancer = sprintf "Balancer", ParanoidPirate.balancer
                    //    let clients = List.init ParanoidPirate.num_clients (fun i -> sprintf "Client %i" i, ParanoidPirate.client)
                    //    let workers = List.init ParanoidPirate.num_workers (fun i -> sprintf "Worker %i" i, ParanoidPirate.worker)
                    //    balancer :: clients @ workers |> List.toArray
                    //    )
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