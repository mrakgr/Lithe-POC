module Main

open System
open System.Threading
open System.Threading.Tasks

open System.Reactive
open System.Reactive.Concurrency
open System.Reactive.Disposables

open NetMQ
open NetMQ.Sockets

[<EntryPoint>]
let main argv = Avalonia.ZeroMQ.Main.main argv
