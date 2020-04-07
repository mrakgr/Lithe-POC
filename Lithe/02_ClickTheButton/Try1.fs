module ClickTheButton.Try1

open System
open System.IO
open System.Windows
open System.Windows.Input
open System.Windows.Controls
open System.Windows.Media
open System.Windows.Media.Imaging
open System.Windows.Shapes
open System.Windows.Documents

open FSharp.Control.Reactive

//let dispatch' = Subject.broadcast
//let dispatch x = dispatch'.OnNext x

// TODO: Forget about cleanup for the time being. This is a proof of concept.
let control c l = let c = c() in List.iter ((|>) c) l; c
let prop s v c = Observable.subscribe (s c) v |> ignore
let event s f c = (s c : IEvent<_,_>).Add (f c)

let w = 
    control Window [
        (fun t -> 
            t.MinHeight <- 300.0
            t.MinWidth <- 300.0
            t.WindowStartupLocation <- WindowStartupLocation.CenterScreen
            t.SizeToContent <- SizeToContent.WidthAndHeight
            t.Content <-
                control Button [
                    (fun btn ->
                        btn.Content <- "_Click Me."
                        btn.Padding <- Thickness(25.0)
                        btn.Margin <- Thickness(10.0)
                        btn.HorizontalContentAlignment <- HorizontalAlignment.Left
                        btn.VerticalContentAlignment <- VerticalAlignment.Top
                        btn.Click.Add(fun x -> MessageBox.Show("Button has been clicked.",t.Title) |> ignore)
                        btn.Focus() |> ignore
                        )
                    ]
            )
        ]
            
[<STAThread>]
let main _ = Application().Run(w)