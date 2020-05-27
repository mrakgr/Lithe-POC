module MinimalExample

open System
open System.Threading
open FSharp.Control.Reactive
open System.Reactive.Disposables

module Messaging =
    open System
    open System.Threading
    open System.Threading.Tasks
    open NetMQ
    open NetMQ.Sockets

    let run l =
        let l = l |> Array.map (fun f -> let poller = new NetMQPoller() in poller, Task.Run(fun () -> f poller : unit))
        Disposable.Create(fun () ->
            let pollers, tasks = Array.unzip l
            pollers |> Array.iter (fun (x : NetMQPoller) -> x.Stop())
            Task.WaitAll(tasks)
            (pollers, tasks) ||> Array.iter2 (fun a b -> a.Dispose(); b.Dispose())
            )

    let inline t a b x rest = a x; let r = rest() in b x; r
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

open Messaging

type Msg = Add of id: int * msg: string
type MsgStart = StartExample
type State = Map<int,int>

let main argv =
    let writeline name = function
        | Some(i,x) -> printfn "%s:%i:%s" name i x
        | None -> printfn "%s:-----" name
    let ignore _ _ = ()
    let l = 
        [|
        "Ventilator", KillSignal.ventilator 1000, writeline
        "Worker 1", KillSignal.worker, ignore
        "Worker 2", KillSignal.worker, ignore
        "Worker 3", KillSignal.worker, ignore
        "Worker 4", KillSignal.worker, ignore
        "Sink", KillSignal.sink, writeline
        |] |> Array.map (fun (a,b,c) -> a,b,c a)

    let create () =
        let agent = FSharpx.Control.AutoCancelAgent.Start(fun mailbox -> async {
            let line_counts = Array.zeroCreate l.Length
            let rec loop () = async {
                let! (Add(i,x)) = mailbox.Receive()
                let count = line_counts.[i]
                l.[i] |> fun (_,_,print) -> print (Some(count, x))
                line_counts.[i] <- count + 1
                do! loop()
                }
            do! loop ()
            })
        l |> Array.mapi (fun i (_,f,_) -> f (fun x -> agent.Post(Add(i,x))))
        |> Messaging.run
        |> Disposable.compose (Disposable.Create(fun () -> l |> Array.iter (fun (_,_,print) -> print None)))
        |> Disposable.compose agent

    while true do
        let d = create()
        Console.ReadKey() |> ignore
        d.Dispose()
    0