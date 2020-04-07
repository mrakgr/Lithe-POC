module StackTenButtons.Try1

open System
open System.Windows
open System.Windows.Input
open System.Windows.Controls
open System.Windows.Controls.Primitives
open System.Windows.Media
open System.Windows.Media.Imaging
open System.Windows.Shapes
open System.Windows.Documents

//let dispatch' = Subject.broadcast
//let dispatch x = dispatch'.OnNext x

// TODO: Forget about cleanup for the time being. This is a proof of concept.
let control c l = let c = c() in List.iter ((|>) c) l; c
let prop s v c = Observable.subscribe (s c) v |> ignore
let event s f c = (s c : IEvent<_,_>).Add (f c)
let content s c = s c
let children' s l c = (s c) l

let w =
    control Window [fun t ->
        t.MinHeight <- 300.0
        t.MinWidth <- 300.0
        t.WindowStartupLocation <- WindowStartupLocation.CenterScreen
        t.SizeToContent <- SizeToContent.WidthAndHeight
        t.Title <- "Stack Ten Buttons"
        t.Content <- control StackPanel [
            (fun pan ->
                pan.Background <- Brushes.Aquamarine
                pan.Margin <- Thickness 10.0
                )
            children' (fun pan l -> pan.Children.Clear(); List.iter (pan.Children.Add >> ignore) l)
                [
                let rng = Random()
                for i=0 to 9 do
                    yield 
                        control Button [fun btn ->
                            btn.Margin <- Thickness 2.0
                            btn.Name <- 'A' + char i |> string
                            btn.FontSize <- rng.Next(10) |> float |> (+) btn.FontSize
                            btn.Content <- sprintf "Button %s says click me!" btn.Name
                            btn.Click.Add(fun args -> MessageBox.Show(sprintf "Button %s has been clicked!" btn.Name,"Button Click") |> ignore)
                            ]
                ]
            ]
        ]

[<STAThread>]
let main _ = Application().Run(w)