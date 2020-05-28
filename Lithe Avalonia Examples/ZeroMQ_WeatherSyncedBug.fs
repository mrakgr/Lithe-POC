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

    let run l = // I've changed this since the other issue.
        let l = l |> Array.map (fun f -> 
            let poller = new NetMQPoller()
            let thread = Thread(ThreadStart(fun () -> f poller),IsBackground=true)
            thread.Start()
            poller, thread)
        Disposable.Create(fun () ->
            l |> Array.iter (fun (poller,thread) -> poller.Stop())
            l |> Array.iter (fun (poller,thread) -> thread.Join(); poller.Dispose())
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

    module ResponseSocket =
        let inline sync_receive_string uri =
            use s = new ResponseSocket()
            bind uri s <| fun () ->
            let r = s.ReceiveFrameString()
            s.SendFrameEmpty()
            r

    module RequestSocket =
        let inline sync_send_string uri x =
            use s = new RequestSocket()
            connect uri s <| fun () ->
            s.SendFrame(x : string)
            s.ReceiveFrameBytes() |> ignore

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
                //init ResponseSocket poller (bind uri_start) <| fun initer ->
                //    for i=1 to num_subs do initer.ReceiveFrameString() |> ignore; initer.SendFrameEmpty()
                for i=1 to num_subs do ResponseSocket.sync_receive_string uri_start |> ignore
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
        Array.append [|"Server", WeatherSynchronized.server, writeline|] <| Array.init WeatherSynchronized.num_subs (fun i -> 
            let i=i+1 in sprintf "Client %i" i, WeatherSynchronized.client (sprintf "%05d" (10000+i)), ignore
            )
        |> Array.map (fun (name,f,print) -> name,f,print name)

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