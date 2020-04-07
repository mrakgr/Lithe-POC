open System
open System.Windows
open System.Windows.Input
open System.Windows.Controls
open System.Windows.Media

open FSharp.Control.Reactive

// TODO: Forget about cleanup for the time being. This is a proof of concept.
let control c l = let c = c() in List.iter ((|>) c) l; c
let prop' s v c = s c v
let prop s v c = Observable.subscribe (s c) v |> ignore
let event s f c = (s c : IEvent<_,_>).Add (f c)

let w =
    let brush = SolidColorBrush(Colors.Black)

    control Window [
        prop' (fun c v -> c.Height <- v) 400.0
        prop' (fun c v -> c.Width <- v) 400.0
        prop' (fun c v -> c.WindowStartupLocation <- v) WindowStartupLocation.CenterScreen
        prop' (fun c v -> c.Background <- v) brush
        event (fun c -> c.MouseMove) (fun t args ->
            let width = t.ActualWidth - 2.0 * SystemParameters.ResizeFrameVerticalBorderWidth
            let height = t.ActualHeight - 2.0 * SystemParameters.ResizeFrameHorizontalBorderHeight - SystemParameters.CaptionHeight

            let ptMouse = args.GetPosition(t)
            let ptCenter = Point(width/2.0, height/2.0)
            let vectMouse = ptMouse - ptCenter
            let angle = atan2 vectMouse.Y vectMouse.X
            let vectEclipse = Vector(width / 2.0 * (cos angle), height / 2.0 * (sin angle))
            let byLevel = 255.0 * (1.0 - min 1.0 (vectMouse.Length / vectEclipse.Length)) |> byte
            brush.Color <- Color.FromRgb(byLevel,byLevel,byLevel)
            )
        ]

[<STAThread>] 
[<EntryPoint>]
let main _ = Application().Run(w)
