Equinox is an event-sourcing engine consisting of an data store tailored to event streams and a projection system for distributing event streams.

![](./Equinox Specification_files/macro)

## 1\. System Model

The system consists of _state machines_ hosted by _microservices_ to perform _operations_ on _entities_. The execution of an operation entails the sequential execution of a state machine. Formally, a [state machine](https://en.wikipedia.org/wiki/Finite-state_transducer) is a tuple: 

<table class="relative-table wrapped confluenceTable" style="width: 43.6782%;margin-left: 240.0px;"><colgroup><col style="width: 6.50264%;"><col style="width: 93.4974%;"></colgroup>

<tbody>

<tr>

<th class="confluenceTh">**`S`**</th>

<td class="confluenceTd">A set of states.</td>

</tr>

<tr>

<th class="confluenceTh">**`I`**</th>

<td class="confluenceTd">A set of inputs.</td>

</tr>

<tr>

<th class="confluenceTh">**`O`**</th>

<td class="confluenceTd">A set of outputs.</td>

</tr>

<tr>

<th class="confluenceTh">**`s<sub><span style="color: rgb(34,34,34);">∅</span></sub>`**</th>

<td class="confluenceTd">An initial state.</td>

</tr>

<tr>

<th class="confluenceTh"><span style="color: rgb(34,34,34);">τ</span></th>

<td class="confluenceTd">A transition relation **`S × I → S × O`** taking pairs of states and inputs to _next_ state and output.</td>

</tr>

</tbody>

</table>

An input **`i <span style="color: rgb(34,34,34);">∈ I</span>`** to a state machine represents a request to perform an action on an entity, identified in the input. A state **`s <span style="color: rgb(34,34,34);">∈</span> S`** corresponds to the state of the entity. Given a state `**s****<sub>n</sub>**` and an input **`i`** the transition function returns state **`s``<sub>n+1</sub>`** where the subscript corresponds to the _version_ of the state. The state machine does not specify how state, concurrency and communication are managed. Instead, these responsibilities are delegated to the hosting microservice. A microservice feeds the input and state to the state machine, and then interprets the resulting state and output. The input to a state machine is derived from messages received by the service performing the operation. The output of a state machine is transformed and possibly enriched to form a response message. The microservice can run concurrent executions of a state machine. State can be managed in several ways. Equinox enables the event-sourcing paradigm for state management as defined below.

## 2\. Event Sourcing

Event-sourcing is a state management paradigm, wherein rather than persisting the state itself, a history of the outputs of the state machine are persisted. Because state machine outputs play a critical role in event-sourcing and have additional semantics associated with them, they are more accurately referred to _events_. From the state machine transition function `**<span style="color: rgb(34,34,34);">τ</span>**` we can factor the following _delta_ function:

<table class="wrapped confluenceTable" style="margin-left: auto;margin-right: auto;"><colgroup><col></colgroup>

<tbody>

<tr>

<th class="confluenceTh">**`<span style="color: rgb(34,34,34);">Δ</span> : S **<span>×</span>** E → S`**</th>

</tr>

</tbody>

</table>

which given a state and an event, returns the next state. This function can be used to reconstitute state based on using a [fold](https://en.wikipedia.org/wiki/Fold_(higher-order_function)) **`fold <span style="color: rgb(34,34,34);">Δ</span> **`s<sub><span style="color: rgb(34,34,34);">∅</span></sub>`**`**. The sequence number of the resulting state instance corresponds to the sequence number of the last event used to derive it. When performing an operation, the history of past events of the state machine is retrieved and folded to recover the most recent state.  It is important that this function is deterministic so that we always fold to the same state. 

Managing state in this way offers several advantages:

*   **Evolution**: A history of outputs can be re-interpreted after the fact, allow the definition of state to evolve. Moreover, downstream systems can provide their own interpretations of events at a later time.
*   **Transactional Orchestration**: Persisting events supports both state management and microservice orchestration in a transactional manner. Rather than persisting state and separately publishing an event, the two responsibilities are consolidated into the event store.
*   **Fault Tolerance**: state machines and events can be replicated to provide fault tolerance and scalability.

### Streams, Logs & Partitions

Event-sourcing involves two types of collections of events, a _stream_ and a _log_. A stream is a sequence of events associated with a particular stream id, which itself corresponds to an entity. A log is a collection of events across all streams in a namespace. Streams are based on an _index_ of the log by the stream id. Equinox imposes a total order of events in a stream, while the ordering of events across streams is imposed by the underlying data store and may be different from the real-time ordering of those events. Typically, the underlying store imposes a total order of events in each _partition_. A database consists of a log per partition.

A log for a single (physical) partition can be visualized as follows:

![](./Equinox Specification_files/log-layout.png "Marvel > Equinox Specification > log-layout.png")

**Figure 2.1: A sequential log as one might represent it in a physical partition of a store** 

CosmosDb provides a _[change feed](https://docs.microsoft.com/en-us/azure/cosmos-db/change-feed)_ facility which affords a mechanism to consume events stored in this way per [partition](https://docs.microsoft.com/en-us/azure/cosmos-db/partition-data) range, including the LSN in the above figure.

An index of streams for this partition can be visualized as follows:

![](./Equinox Specification_files/streams-layout.png "Marvel > Equinox Specification > streams-layout.png")

**Figure 2.2: An index of streams derived from the log above.**

A stream index serves two central roles: 1) it allows lookup of events by a stream id and 2) it provides a boundary of sequential ordering.

### Aggregates

The ordering and concurrency guarantees provided by streams make them an ideal candidate for storing domain events for [_aggregates_](https://martinfowler.com/bliki/DDD_Aggregate.html). In particular, the aggregate forms a "transactional consistency boundary" which maps naturally to a stream. The state machine definition above can be refined to include in the output type a case to specify whether an output (event) should be committed, or only responded with. It is common for example to not produce events when inputs do not result in a change the state of the aggregate - such as errors, or exceptions- referred to as [idempotent processing](https://en.wikipedia.org/wiki/Idempotence). The _behaviors_ of an aggregate are specified as a state machine - inputs are requests to perform an operation, outputs are events resulting for performing an operation. A request to perform an operation on an aggregate are often times prompted by other events. The projection system, described below, can be used to orchestrate interactions between aggregates - the events of one collection of aggregates can drive operations on downstream aggregates.

### Example

A state machine can model a stateful application such as a shopping cart, where states correspond to states of the shopping carts, inputs are requests to update the shopping cart, outputs are events indicating that a request has been performed, the initial state is an empty cart, and the transition relation embodies the business logic defining how requests to update a cart apply to carts in a particular state. An event for a shopping cart might be **`ItemAdded`**, and the input / command leading to it might be **`AddItem`**.

## 3\. Equinox API

The following defines the Equinox API.

See also: [F# API](https://jet-tfs.visualstudio.com/Jet/Marvel/_git/marvel?path=%2Fsrc%2Fmarvel-equinox%2FMarvel.Equinox%2FEquinox.fsi&version=GBxray_clean&fullScreen=true&_a=contents) ([OSS version](https://github.com/jet/equinox/blob/495d577584a22c7cd9dc7a0aac12419787848c57/src/Equinox.Cosmos/Cosmos.fs#L1118)).

### Types

<table class="wrapped confluenceTable"><colgroup><col style="width: 89.0px;"><col style="width: 1239.0px;"></colgroup>

<tbody>

<tr>

<th class="confluenceTh">Type</th>

<th class="confluenceTh">Definition</th>

</tr>

<tr>

<td class="confluenceTd">**`Conn`**</td>

<td class="confluenceTd">Represents a client connection to the data store. The connection may store session metadata, and other configuration information. One connection object should be created per application instance.</td>

</tr>

<tr>

<td colspan="1" class="confluenceTd">**`StreamId`**</td>

<td colspan="1" class="confluenceTd">A unique identifier of a stream in a collection.</td>

</tr>

<tr>

<td colspan="1" class="confluenceTd">**`SN`**</td>

<td colspan="1" class="confluenceTd">A natural number corresponding to the sequence number of an event in a stream. The events in a stream form a proper [sequence](https://en.wikipedia.org/wiki/Sequence), such that a stream defines a partial function from integers to events.</td>

</tr>

<tr>

<td colspan="1" class="confluenceTd">**`LSN`**</td>

<td colspan="1" class="confluenceTd">A sequence number of the event within a partition of a collection.</td>

</tr>

<tr>

<td colspan="1" class="confluenceTd">**`AsyncSeq`**</td>

<td colspan="1" class="confluenceTd">An (asynchronous) sequence.</td>

</tr>

<tr>

<td colspan="1" class="confluenceTd">**`EquinoxEvent`**</td>

<td colspan="1" class="confluenceTd">Represents an individual event.</td>

</tr>

<tr>

<td class="highlight-red confluenceTd" colspan="1" data-highlight-colour="red">**`AppendError.InvalidExpectedSequenceNumber`**</td>

<td colspan="1" class="confluenceTd">Indicates that the expected version specified in the add operation didn't match the version of the stream at the time of application. No events written in this case.</td>

</tr>

</tbody>

</table>

### Event

<table class="wrapped relative-table confluenceTable" style="width: 95.6358%;"><colgroup><col style="width: 7.00787%;"><col style="width: 92.9921%;"></colgroup>

<tbody>

<tr>

<th class="confluenceTh">

Field

</th>

<th class="confluenceTh">Definition</th>

</tr>

<tr>

<td class="confluenceTd">

**`StreamId`**

</td>

<td class="confluenceTd">The id of the stream to which the event belongs.</td>

</tr>

<tr>

<td colspan="1" class="confluenceTd">**`SN`**</td>

<td colspan="1" class="confluenceTd">The sequence number of the event in the stream.</td>

</tr>

<tr>

<td colspan="1" class="confluenceTd">**`LSN`**</td>

<td colspan="1" class="confluenceTd">The LSN of the event.</td>

</tr>

<tr>

<td colspan="1" class="confluenceTd">**`Timestamp`**</td>

<td colspan="1" class="confluenceTd">The time at which the event was generated.</td>

</tr>

<tr>

<td colspan="1" class="confluenceTd">**`Payload`**</td>

<td colspan="1" class="confluenceTd">A payload of the event, either a JSON byte array, or a binary array to be base64 encoded.</td>

</tr>

</tbody>

</table>

### Operations

<table class="relative-table wrapped confluenceTable" style="width: 100.0%;"><colgroup><col style="width: 47.8327%;"><col style="width: 52.1673%;"></colgroup>

<tbody>

<tr>

<th colspan="1" class="confluenceTh">Operation</th>

<th colspan="1" class="confluenceTh">Definition</th>

</tr>

<tr>

<td class="confluenceTd">**`get : Conn → StreamId → SN → AsyncSeq<Event[]>`**</td>

<td class="confluenceTd">

Given a connection, stream identifier and a sequence number, returns a sequence of events starting at the sequence number.

</td>

</tr>

<tr>

<td colspan="1" class="confluenceTd">**`getBack : Conn → StreamId → SN → AsyncSeq<Event[]>`**</td>

<td colspan="1" class="confluenceTd">Given a connection, stream identifier and a sequence number, returns an async sequence of events in the stream backwards starting from the specified sequence number</td>

</tr>

<tr>

<td class="confluenceTd">**`add : Conn → StreamId → SN → Event[] → Async<Result<SN, AppendError>>`**</td>

<td class="confluenceTd">Given a connection, stream identifier, expected sequence number (expected version) and a set of events, adds the events to the stream. If the specified expected version number doesn't match the stream, an error is returned and the events are not added to the stream.</td>

</tr>

</tbody>

</table>

See the [F# API](https://jet-tfs.visualstudio.com/Jet/Marvel/_git/marvel?path=%2Fsrc%2Fmarvel-equinox%2FMarvel.Equinox%2FEquinox.fsi&version=GBxray_clean&fullScreen=true&_a=contents) for additional operations.

## 4\. Sequence Numbers, Versions, Ordering

Each stream in Equinox forms a finite, totally ordered sequence of events. The sequence is contiguously indexed by natural numbers, starting with 0\. The latest sequence number in the stream is the stream _version_ or _position_. While events in a particular stream are totally ordered, no order is specified for events across streams. However, individual events can explicitly embed ordering information with respect to events from other streams, thereby forming a partial order of all events. (Equinox does not currently operate on such causality metadata. In the future, Equinox may provide a notion of sequence based on a looser order). Furthermore, Equinox allows events to specify a key which is used to determine the _logical _partition in which the event is written. This makes it possible to rely on the total ordering among streams grouped within that logical partition of the log when consumed via the change feed.

## 5\. Concurrency

Multiple executions of the state machine can run concurrently. In particular, multiple executions on inputs corresponding to the same entity can be run concurrently. Since streams are sequences, a consistent order must be imposed for written events. In Equinox, this is achieved through [optimistic concurrency control](https://en.wikipedia.org/wiki/Optimistic_concurrency_control) (OCC). In OCC, any write operation is accompanied by an _expected version number that_ is matched against the current version at the time of application. In case of a version mismatch, the write is failed and the operation must be retried, taking into account the conflicting events. No concurrency control mechanism for cross-stream operations is provided (However, this can be implemented using CosmosDB transactions, see below).

Note that Equinox requires the use of OCC, but also provides an analog to the **`ExpectedVersion.Any`** construct in EventStore with the **`appendAtEnd`** operation. The performance and reliability of appendAtEnd is equivalent to that of a normal append - writes get assigned Indexes only at the point where the CosmosDb stored procedure applies a set of events being appended, without necessitating any querying of the stream to obtain the next index (with the attendant race condition thereby implied).

TODO: diagram of sequence diagram for operations to show start event, then receive/application event, etc.

## 6\. Transactions

Equinox does not provide cross-stream transactions. (However, since we're on CosmosDB, it would be _technically_ feasible to expose that functionality). 

## 7\. Cosmos DB

Equinox stores events in CosmosDB, as a document per set of events written, keyed by the stream id and event index. Equinox has inherited geo-replication and CosmosDB consistency models as described in greater detail below.

### Consistency Models

CosmosDB <span class="inline-comment-marker" data-ref="731efccc-43da-4c2b-b715-754cea507679">offers</span> several consistency models, which place restrictions on the histories of write operations as applied to replicas of data nodes and as observable by a given client. More precisely, a consistency model defines a set of permitted execution orders with respect to a client node and data replica nodes. Stronger consistency models impose a greater restriction on execution orders than weaker consistency models.

#### Histories & Write Sequences

A _history_ **`H`** is a finite sequence of _events_. _Operations_ are matching pairs of start and complete events. A history **`H`** provides a partial order of operations **`<<sub>H</sub>`** such that **`op1 < op2`** if **`res(op1) < inv(op2)`** where **`res`** and **`inv`** are the response and invocation events for an operation. The processes in our case are either client nodes or replica nodes. Client process histories are assumed to be _sequential,_ which means the order of events at a process is total. By contrast, the order of events on a replica node is in general not total - some operations overlap and are therefore _concurrent_. A subhistory at process **`p`** is defined as **`H|p`**. Two histories **`H`** and **`H'`** are _equivalent_ if for each client node process **p** we have `**H|p** = **H'|p**`. Two sequential histories are equivalent if they have the same events, in the same order. In addition to histories we consider sequences of writes applied at a replica up to and including a given time, which we define as **`WS(r,t)`** where **`r`** is the data replica and **`t`** is time.

#### Linearizability

Linearizability is the strongest consistency model. It states that operations _appear_ to take place instantaneously between the invocation and response. The mention of "appear instantaneously" is so that we can identify an event with an operation taking effect, and moreover, we can assign a sequential ordering to these events. More precisely, when operations don't overlap - meaning that one completes before another starts - the order across replicas matches the real time order of these operations. When there is an overlap, the writes are ordered in such a way that there exists a sequential history with the same outcome. In other words, suppose we have a complete history `**H**` - there are no pending operations. The history is _linearizable_ if there is a sequential history `**S**` such that `**H = S**` (as defined above) and the order of events in `**H**` is a subset of the order of events in **`S`**. The history `**S**` is a linearization of `**H**`, and there may be multiple such linearizations due to non-determinism.

Next, we discuss several consistency models for systems where replication occurs asynchronously. These are client-centric consistency models because rather than maintaining consistency for the entire system, they try to maintain consistency for individual client connections.

#### Read-Your-Writes (RYW)

Under RYW, if a client commits a write operation, the next read operation for the same key, by the same connection, will return that write. This does not need to hold across connections. So it is possible that a different connection will read an earlier state, because the request may be routed to a node which has not yet applied the writes leading to the state observed by the read.

![](./Equinox Specification_files/read-your-writes-consistency.png "Marvel > Equinox Specification > read-your-writes-consistency.png")

**Figure 1: Read-your-writes**

In Figure 1 we see how Session 1 is able to read its write **`W(x=1)`**. However, this guarantee does not extend to Session 2\. It could be violated by Session 1 if it directed the read to the lagging replica 2.

#### Monotonic-Reads (MR)

Successive read operation return values at a greater or equal version across all data items observed by the session.

![](./Equinox Specification_files/monotonic-reads-consistency.png "Marvel > Equinox Specification > monotonic-reads-consistency.png")

**Figure 2: Monotonic-Reads**

In Figure 2, both replicas start with value 0 for object x, but another session (not pictures) applies W(x=1) to replica 1\. The third read operation is routed to the lagging replica 2 which results in a non-monotonic read since an earlier value of object x was observed. This anomaly can be prevented by always reading from the same replica (assuming the replica is itself monotonic), and also by keeping a local cache which keeps track of past reads.

#### Monotonic-Writes (MW)

Write operations are applied sequentially, in a monotonically increasing order.

![](./Equinox Specification_files/monotonic-writes-consistency.png "Marvel > Equinox Specification > monotonic-writes-consistency.png")

**Figure 3: Monotonic-Writes**

#### Writes-Follow-Reads (WFR) 

Write operations are routed to a greater or equal version of a value that a prior read was performed on.

![](./Equinox Specification_files/writes-follow-reads-consistency.png "Marvel > Equinox Specification > writes-follow-reads-consistency.png")

**Figure 4: Writes-Follow-Reads**

`**WFR**` is similar to **`MW`**, however the difference is that it relates writes to reads during the same session, rather than only governing the application of writes in a session. It is possible to have **`WFR``∧ `****`!MW`** in the following way. A session could read **`[x=1] `**but then perform two overlapping writes such that they both go to the same replica, but are applied out of order. Conversely, it is possible to have !**`WFR``∧ `****`MW`** by applying writes to the same node, sequentially on after the other, but on a different node than where the read was served from. (Also, it is possible to distinguish among cases wherein a database must see all previous writes or only the latest).

#### Session Consistency

Session consistency is a client-centric consistency model, in that guarantees are provided with respect to a particular client instance. A salient characteristic of session consistency is the notion of _affinity_ in that, given a set of replicas for a data item, a session will perform operations on the same replica for the given data item to ensure consistency. While this effectively reduces the availability of the system, it offers a tradeoff indispensable in a geo-replicated system. Session consistency is defined as **`RYW ∧ MR ∧ MR ∧ WFR`**.

Causal consistency ensures that causally related subsequences of operations are applied in the same order across all replicas.

#### **Consistent Prefix**

The sequence of writes _observed_ by client is a sub-sequence of the _applied_ writes.

#### **Eventual Consistency**

Eventual consistency (EC) is a liveness property stating that eventually, replicas will converge to the same state. This relies on 1) the writes being applied in a consistent order, and that 2) all writes are applied across all replicas. With EC, CosmosDB makes few guarantees other than eventual convergence in the absence of new writes, but provides the best performance and availability. It is possible for a client to observe writes out-of-order.

### Geo-Replication

CosmosDB can operate across data centers under certain consistency guarantees. At a given point in time, one of the data centers serves as the master and the other as the warm standby. A data-center failover can trigger automatically or be requested manually.

See [CosmosDB geo-replication](https://docs.microsoft.com/en-us/azure/cosmos-db/distribute-data-globally) documentation for more details.

## 8\. Snapshotting

Snapshotting is a state caching mechanism used to reduce the number of events that need to be read from a stream to perform an operation and can be generated using the _delta_ function <span style="color: rgb(34,34,34);">Δ</span><span style="color: rgb(34,34,34);"> defined above. When performing an operation on an aggregate, the latest snapshot `**s<span style="font-size: 11.6667px;"><sub>latest</sub></span>**` is read. Then then events since sequence number `**n**` of the latest snapshot can read from the event store, or the operation can be attempted speculatively with the expected version `**n**`. Snapshots are broadly classified along three dimensions: generator, storage and interval. The generator can be the client itself, or it can be done using a projection. Snapshots can be stored in the same event store as the events themselves, or an entirely different data store. Finally, snapshots can be generated for each event or on a fixed interval. </span>

* * *

## 9\. Projections

Equinox provides a projection system, which projects events into Kafka. An individual _projection_ is a mapping of events in the log to topics in Kafka using a few pre-defined predicates (stream category, event type). This can be used as a distribution/orchestration mechanism for events, both as a medium, but also in terms of a classification of events in a domain specific way. A _projector_ is a service that emits projections for an individual upstream event store.

The architecture of the Equinox projection system can be visualized as follows:

![](./Equinox Specification_files/equinox_projection_system.png "Marvel > Equinox Specification > equinox_projection_system.png")

**Figure 9.1: The Equinox Projection System**

A projection definition consists of the following items:

<table class="wrapped confluenceTable"><colgroup><col style="width: 106.0px;"><col style="width: 699.0px;"></colgroup>

<tbody>

<tr>

<th class="confluenceTh">Item</th>

<th class="confluenceTh">Description</th>

</tr>

<tr>

<td class="confluenceTd"><span style="font-family: monospace;">**Source**</span></td>

<td class="confluenceTd">The upstream event store to project.</td>

</tr>

<tr>

<td class="confluenceTd">**`Name`**</td>

<td class="confluenceTd">The name of the projection. This should be domain specific and provide an indication of the events contained.</td>

</tr>

<tr>

<td colspan="1" class="confluenceTd">**`Topic`**</td>

<td colspan="1" class="confluenceTd">The Kafka topic to project to.</td>

</tr>

<tr>

<td colspan="1" class="confluenceTd">**`Predicate`**</td>

<td colspan="1" class="confluenceTd">

Defines the subset of events from the log that are to be projected. Currently, this is restricted to the following:

*   **`Category`**: based on a stream id prefix
*   **`EventType`**: based on the event type 
*   **`Union`**: a composite of the the predicates above.

</td>

</tr>

</tbody>

</table>

In the future, we will enhance the Equinox projection system to support transformations of events, as well as a built-in state storage mechanism to allow, for example, aggregation.

### Ordering & Delivery Semantics

The Equinox projection system ensures that events belonging to a stream are always routed to the same Kafka partition, and guarantees _at-least-once_ delivery semantics. In particular, a consumer may observe the same event twice and are therefore required to be _idempotent_. Ordering is modulo idempotence in that it is possible to receive an event out of order if the event has already been processed. Ordering and at-least-once guarantees are ensured by the cooperation of the projector.

The [Luminus](https://my-jet.atlassian.net/wiki/spaces/MARVEL/pages/52035587/Luminus-Marvel+Projector+Design) platform verifies these guarantees and generates alerts in case of violations.

### Projector Progress

The projector is a consumer of the log (change feed) of the upstream event store and its progress is marked by the sequence number (LSN) in the log. See the [Equinox Projector Dashboard](https://jet.splunkcloud.com/en-US/app/search/marvel_eqx_projector?form.env=prod-marvel&form.time.earliest=-60m%40m&form.time.latest=now).

### Offset Index

Messages in Kafka are identified by topic, partition and offset - **`(T,P,O)`**. As a result, a mapping mechanism between the `**LSN**` of the event and **`(T,P,O)`** is required. This is done by the _offset index_which is a per-region mapping **`LSN → (T,P,O) option`**, returning `**None**` if the corresponding message does not exist in Kafka. Note that a unique mapping for each `**LSN**` is not provided. Instead, mappings are recorded at a particular resolution level (ie 30 seconds). The offset index guarantees that the **`(T,P,O)`** returned for **LSN i** will correspond to messages with **LSN j **in Kafka such that `**j **<span style="color: rgb(34,34,34);font-weight: bold;">≤ </span>`**i** thereby adhering to at-least-once delivery semantics.An offset index is associated with each Kafka cluster used for projection. The offset index also supports reverse lookup  **`(T,P,O)`****` → LSN`** which is used to map consumer progress in Kafka to progress with respect to the change feed, as described below.

See: [Equinox HTTP API](https://my-jet.atlassian.net/wiki/spaces/MARVEL/pages/62324742/Equinox+HTTP+API)

### Consumer Progress

Projection consumers can consume the Kafka topics directly using any client that commits offsets to Kafka (ie using protocol v0.9 or greater). In order to support failover to a Kafka cluster in another region, the Equinox projection system runs a service (**`Consumer Progress Mapper`**) which observes Kafka consumer offsets committed with respect to projected topics and automatically maps those offsets to their respective **`LSNs`**. This mapping, **`Consumer → LSNs`**, is stored in CosmosDB, ensuring durability and geo-redundancy. Equinox provides an HTTP API which, given an consumer's group id and a target region, returns the corresponding Kafka offsets for that partition. Each time the consumer starts, it should request offsets from the API, commit them and then start consumption. This way, the consumer can resume in any region.

See: [Equinox HTTP API](https://my-jet.atlassian.net/wiki/spaces/MARVEL/pages/62324742/Equinox+HTTP+API)

### Fail Over

The projection system provides tolerance against failure of a Kafka cluster in an single region, by running the projection system across regions in parallel. See [Consumer Progress](https://my-jet.atlassian.net/wiki/pages/createpage.action?spaceKey=MARVEL&title=Consumer+Progress&linkCreation=true&fromPageId=32833923) for instructions on how to failover consumers.

### Replay

Due to strict retention policies we typically apply in Kafka, a consumer may not be able to retrieve a desired event from a Kafka projection. In this case, it is possible to request a replay or _backfill_ from Equinox. To request a backfill, a desired range of `**LSNs**` is provided to an HTTP API along with a pre-defined Kafka topic, and the Equinox replay system will populate that topic with the desired range.

See the [Equinox Replay HTTP API](https://my-jet.atlassian.net/wiki/spaces/MARVEL/pages/62324742/Equinox+Replay+HTTP+API) for details.

## 10\. Getting Started

To get started with Equinox, reach out to the Marvel team. Marvel handles the configuration of the respective CosmosDB and Kafka resources, as well as configuration of the projection system.

See also: [Equinox Getting Started Guide](https://my-jet.atlassian.net/wiki/spaces/MARVEL/pages/149258312/Equinox+Getting+Started).

### Using the Library

To use the library, see [http://github.com/jet/equinox](http://github.com/jet/equinox) and [http://github.com/jet/dotnet-templates](http://github.com/jet/dotnet-templates)

### Migration

The Equinox Migrator service can migrate an existing EventStore system to Equinox. Migration consists of several phases:

#### Phase 1: Backfill

In this initial migration phase, the migrator replicates an EventStore into Equinox. This can occur with respect to a live EventStore, or a replica. The migrator can split an EventStore into multiple Equinox databases based on EventStore stream categories. It may be desirable to split an EventStore into multiple collections to decouple provisioning, billing, etc.

#### Phase 2: Real-Time Reads

Once the backfill is sufficiently up to date, the migrator can be pointed to a real-time node, if not already. Throttling can be considered at this point in order to prevent putting to much load on the EventStore. Once the real-time migration is in progress, the system can start serving reads from Equinox, while writes still go to the EventStore. The system can read a stream from Equinox, then read the remainder from EventStore. Measurements can be made to assess the lag in preparation for a fail-over. The system may also choose to perform reads in parallel to ensure that Equinox returns a consistent prefix of events from EventStore.

Meanwhile, Marvel also deploys the Luminus system for the migrator, which ensures all events in the EventStore are migrated to Luminus in the correct order.

#### Phase 3: Failover

Once Equinox meets all performance requirements and the events are sufficiently up to date, a fail-over can be planned. The fail-over process is similar to a regular EventStore regional failover, however in addition, the system must switch the writing mechanism.

After fail-over, the replication channel between EventStore and Equinox can be reversed in order to provide failback to EventStore.