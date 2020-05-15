# Lithe

A successful proof of concept implementation of the MVU (Elm) pattern using just reactive extensions. Check out the [early April 2020 commits](https://github.com/mrakgr/The-Spiral-Language/commit/47548e25f149ad3179fe7d6f243bd0e80e7299f8) on the Spiral language repo for a blow by blow account of what I was trying to do here.

The examples in the `Lithe` project folder use WPF + .NET Core 3.1.

## Writeup

Having studied Rx books for a while, I started this by picking up some [old examples](https://github.com/mrakgr/Exercises/tree/master/Fsharp%20Exercises/Applications%20Markup%20Code%20Part%201) from Charles Petzold's 2006 WPF book and tried my hand at abstracting them. I actually failed at this back in 2016, so I am quite happy to report that things went well this time.

The first 3 examples are by Petzold while `04_ABasicExample` and `05_CounterApp` are from the [Fabulous](https://fsprojects.github.io/Fabulous/) site.

Chronologically, the files show the evolution of this proof of concept library, but if I were to pick just one as highlight it would be [`CounterApp.Try1`](https://github.com/mrakgr/Lithe-POC/blob/master/Lithe/05_CounterApp/Try1.fs). While `Try2` is a more direct translation of the Fabulous example, I feel that `Try1` works better with reactive extensions. The way the timer is implemented in it is just better than in the Fabulous example.

`CounterApp.Try1` demonstrates everything an UI library would need such as:

1) Segregation of the view and the state. This is the MVU pattern.
2) Declarative ability to define nested controls.
3) The ability to handle side effects using a separate pipeline.

The architecture of this is quite solid overall. Much better than putting UI at the top and then having events mutate it. Being shown that something like this is possible with reactive extensions would surely have astonished the me of 4 years ago.

Today there are like UI libraries like Fabulous, but even so since there is no need to do diffing of a virtual DOM this approach would have a performance edge on it since it compiles to reactive combinators directly and therefore has no need for that. And while reactive combinators are more difficult to use, they are also more powerful than what Fabulous allows.

To me this is a confirmation that the subject of functional reactive programming is worth studying. Here I gave it a try at creating a smallish UI library in order to redeem myself for my poor 2016 performance at making UIs, but in the future I will be bringing these techniques to bear on doing editor support for the Spiral language. Having access to an UI library like Fabulous would not be useful for that, but with some effort I might be able to reuse the MVU pattern using reactive combinators even in such a vastly different domain. If that turns out to be a success, it will all be thanks to me going through the motions that I did here.

---

**Update 5/15/2020**: Gtk and Avalonia examples have been added in their respective project folders.

As a short review, between the two Avalonia is the one you should use. The primary reason is Gtk is really bad at doing dynamic layouts and is really finicky about the order the controls get added to their parents. I actually couldn't use the usual design and had to CPS it another level. Even then, the framework still feels broken. In addition to having to dispose of the controls manually as they are native, none of the bindings have descriptions. Doing elementary things like changing the font size is something I could not figure out how to do in Gtk. Also all the Widgets are a pain to use as they take special constructor arguments which adds further difficult in creating reactive abstractions for it. It is an ancient piece of crap.

Avalonia is pretty nice - it is quite similar to WPF except more streamlined. I'll be using it as my UI library of choice from here on out.

I am not done with the examples yet. The reason why I am interested in UIs again is because I am currently studying [ZeroMQ's guide](http://zguide.zeromq.org/page:all) and it is really awkward to work with the command line for all the examples. Instead of doing it from the terminal(s), it occurred to me that this is the ideal time to try wrapping them in a UI. Usually I would not do this, but since I've finally figured out how to handle them using reactive combinators, it is the ideal time to put that knowledge to good use.

Once I am done studying ZeroMQ I will have everything I need to start work on editor support for Spiral.