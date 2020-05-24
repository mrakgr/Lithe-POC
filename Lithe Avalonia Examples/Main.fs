module Main

open System
open System.Threading
open System.Threading.Tasks

open System.Reactive
open System.Reactive.Subjects
open System.Reactive.Linq
open System.Reactive.Concurrency
open System.Reactive.Disposables
open FSharp.Control.Reactive

open NetMQ
open NetMQ.Sockets

[<EntryPoint>]
let main argv = Avalonia.ZeroMQ.Main.main argv
