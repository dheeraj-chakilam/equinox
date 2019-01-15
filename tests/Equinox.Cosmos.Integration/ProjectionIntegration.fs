﻿module ProjectionIntegration

open Equinox.Cosmos.Integration
open Equinox.Cosmos.Projection
open Equinox.Cosmos.Projection.Projector
open Equinox.Cosmos.Projection.Route
open Swensen.Unquote
open System

type Tests(testOutputHelper) =
    inherit TestsWithLogCapture(testOutputHelper)
    let log = base.Log

    [<AutoData(SkipIfRequestedViaEnvironmentVariable="EQUINOX_INTEGRATION_SKIP_COSMOS")>]
    let projector () = Async.RunSynchronously <| async {

        let predicate = Predicate.All
                                  //EventTypeSelector "<c>"
                                  //CategorySelector "<first token before - in p>"
                                  //StreamSelector "<p>"

        let projection = {
            name = "test"
            topic = "xray-telemetry"
            partitionCount = 8
            predicate = predicate
            collection = "michael"
            makeKeyName = None
            partitionKeyPath = None
          } 

        let startPositionStrategy = ChangefeedProcessor.StartingPosition.FromJsonString "{\"Case\":\"ResumePrevious\"}"
        let pub = {
            equinox = "equinox-test-ming"
            databaseEndpoint = Uri("<redacted>")
            databaseAuth = "<authKey>"
            collectionName = "michael"
            database = "equinox-test"
            changefeedBatchSize = 100
            projections = [|projection|]
            region = "eastus2"
            kafkaBroker = "<redacted>"
            clientId = "projector"
            startPositionStrategy = startPositionStrategy
            progressInterval = 30.0
        }

        let! res = Projector.go log pub
        printf "%O" res
        test <@ 1 = 1 @>
    }