module Server.Tests

open Expecto

open Shared
open Server

let server =
    []

let all = testList "All" [ Shared.Tests.shared; ]

[<EntryPoint>]
let main _ = runTestsWithCLIArgs [] [||] all