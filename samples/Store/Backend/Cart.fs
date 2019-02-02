module Backend.Cart

open Domain

type Service(log, resolveStream) =
    let (|AggregateId|) (id: CartId) = Equinox.AggregateId ("Cart", CartId.render id)
    let (|Stream|) (AggregateId id) = Cart.Handler(log, resolveStream id)

    member __.FlowAsync (Stream stream, flow, ?prepare) =
        stream.FlowAsync(flow, ?prepare = prepare)

    member __.Execute (Stream stream) command =
        stream.Execute command

    member __.Read (Stream stream) =
        stream.Read