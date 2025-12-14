module Client.Tests

open Fable.Mocha

open Index
open Shared
open SAFE

let client =
    []

let all =
    testList "All" [
        #if FABLE_COMPILER // This preprocessor directive makes editor happy
        Shared.Tests.shared
#endif
                // client
    ]

[<EntryPoint>]
let main _ = Mocha.runTests all