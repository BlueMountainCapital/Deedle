﻿namespace FSharp.DataFrame

open System
open System.ComponentModel
open System.Collections.Generic
open FSharp.DataFrame.Internal
open FSharp.DataFrame.Indices
open FSharp.DataFrame.Keys
open FSharp.DataFrame.Vectors
open FSharp.DataFrame.VectorHelpers

/// This enumeration specifies joining behavior for `Join` method provided
/// by `Series` and `Frame`. Outer join unions the keys (and may introduce
/// missing values), inner join takes the intersection of keys; left and
/// right joins take the keys of the first or the second series/frame.
type JoinKind = 
  /// Combine the keys available in both structures, align the values that
  /// are available in both of them and mark the remaining values as missing.
  | Outer = 0
  /// Take the intersection of the keys available in both structures and align the 
  /// values of the two structures. The resulting structure cannot contain missing values.
  | Inner = 1
  /// Take the keys of the left (first) structure and align values from the right (second)
  /// structure with the keys of the first one. Values for keys not available in the second
  /// structure will be missing.
  | Left = 2
  /// Take the keys of the right (second) structure and align values from the left (first)
  /// structure with the keys of the second one. Values for keys not available in the first
  /// structure will be missing.
  | Right = 3


/// This enumeration specifeis the behavior of `Union` operation on series when there are
/// overlapping keys in two series that are being unioned. The options include prefering values
/// from the left/right series or throwing an exception when both values are available.
type UnionBehavior =
  /// When there are values available in both series that are being unioned, prefer the left value.
  | PreferLeft = 0
  /// When there are values available in both series that are being unioned, prefer the right value.
  | PreferRight = 1
  /// When there are values available in both series that are being unioned, raise an exception.
  | Exclusive = 2

// ------------------------------------------------------------------------------------------------
// Series
// ------------------------------------------------------------------------------------------------

/// Represents an untyped series with keys of type `K` and values of some unknown type
/// (This type should not generally be used directly, but it can be used when you need
/// to write code that works on a sequence of series of heterogeneous types).
type ISeries<'K when 'K : equality> =
  /// Returns the vector containing data of the series (as an untyped vector)
  abstract Vector : FSharp.DataFrame.IVector
  /// Returns the index containing keys of the series 
  abstract Index : IIndex<'K>
  /// Attempts to get the value at a specified key and return it as `obj`
  abstract TryGetObject : 'K -> OptionalValue<obj>


/// The type `Series<K, V>` represents a data series consisting of values `V` indexed by
/// keys `K`. The keys of a series may or may not be ordered 
and Series<'K, 'V when 'K : equality>
    ( index:IIndex<'K>, vector:IVector<'V>,
      vectorBuilder : IVectorBuilder, indexBuilder : IIndexBuilder ) as this =
  
  member internal x.VectorBuilder = vectorBuilder
  member internal x.IndexBuilder = indexBuilder

  // ----------------------------------------------------------------------------------------------
  // Series data
  // ----------------------------------------------------------------------------------------------

  /// Returns the index associated with this series. This member should not generally
  /// be accessed directly, because all functionality is exposed through series operations.
  ///
  /// [category:Series data]
  member x.Index = index

  /// Returns the vector associated with this series. This member should not generally
  /// be accessed directly, because all functionality is exposed through series operations.
  ///
  /// [category:Series data]
  member x.Vector = vector

  /// Returns a collection of keys that are defined by the index of this series.
  /// Note that the length of this sequence does not match the `Values` sequence
  /// if there are missing values. To get matching sequence, use the `Observations`
  /// property or `Series.observation`.
  ///
  /// [category:Series data]
  member x.Keys = seq { for key, _ in index.Mappings -> key }

  /// Returns a collection of values that are available in the series data.
  /// Note that the length of this sequence does not match the `Keys` sequence
  /// if there are missing values. To get matching sequence, use the `Observations`
  /// property or `Series.observation`.
  ///
  /// [category:Series data]
  member x.Values = seq { 
    for _, a in index.Mappings do 
      let v = vector.GetValue(a) 
      if v.HasValue then yield v.Value }

  /// Returns a collection of observations that form this series. Note that this property
  /// skips over all missing (or NaN) values. Observations are returned as `KeyValuePair<K, V>` 
  /// objects. For an F# alternative that uses tuples, see `Series.observations`.
  ///
  /// [category:Series data]
  member x.Observations = seq {
    for k, a in index.Mappings do
      let v = vector.GetValue(a)
      if v.HasValue then yield KeyValuePair(k, v.Value) }

  /// [category:Series data]
  member x.IsEmpty = Seq.isEmpty index.Mappings

  /// [category:Series data]
  member x.KeyRange = index.KeyRange

  // ----------------------------------------------------------------------------------------------
  // Accessors and slicing
  // ----------------------------------------------------------------------------------------------

  /// [category:Accessors and slicing]
  member x.GetSubrange(lo, hi) =
    let newIndex, newVector = indexBuilder.GetRange(index, lo, hi, Vectors.Return 0)
    let newVector = vectorBuilder.Build(newVector, [| vector |])
    Series(newIndex, newVector, vectorBuilder, indexBuilder)

  /// [category:Accessors and slicing]
  [<EditorBrowsable(EditorBrowsableState.Never)>]
  member x.GetSlice(lo, hi) =
    let inclusive v = v |> Option.map (fun v -> v, BoundaryBehavior.Inclusive)
    x.GetSubrange(inclusive lo, inclusive hi)

  /// Returns a new series with an index containing the specified keys.
  /// When the key is not found in the current series, the newly returned
  /// series will contain a missing value. When the second parameter is not
  /// specified, the keys have to exactly match the keys in the current series
  /// (`Lookup.Exact`).
  ///
  /// ## Parameters
  ///
  ///  * `keys` - A collection of keys in the current series.
  ///
  /// [category:Accessors and slicing]
  member x.GetItems(keys) = x.GetItems(keys, Lookup.Exact)

  /// Returns a new series with an index containing the specified keys.
  /// When the key is not found in the current series, the newly returned
  /// series will contain a missing value. When the second parameter is not
  /// specified, the keys have to exactly match the keys in the current series
  /// (`Lookup.Exact`).
  ///
  /// ## Parameters
  ///
  ///  * `keys` - A collection of keys in the current series.
  ///  * `lookup` - Specifies the lookup behavior when searching for keys in 
  ///    the current series. `Lookup.NearestGreater` and `Lookup.NearestSmaller`
  ///    can be used when the current series is ordered.
  ///
  /// [category:Accessors and slicing]
  member x.GetItems(keys, lookup) =    
    let newIndex = indexBuilder.Create<_>(keys, None)
    let newVector = vectorBuilder.Build(indexBuilder.Reindex(index, newIndex, lookup, Vectors.Return 0), [| vector |])
    Series(newIndex, newVector, vectorBuilder, indexBuilder)

  ///
  /// [category:Accessors and slicing]
  member x.TryGetObservation(key, lookup) =
    let address = index.Lookup(key, lookup, fun addr -> vector.GetValue(addr).HasValue) 
    match address with
    | OptionalValue.Missing -> OptionalValue.Missing
    | OptionalValue.Present(key, addr) -> vector.GetValue(addr) |> OptionalValue.map (fun v -> KeyValuePair(key, v))

  ///
  /// [category:Accessors and slicing]
  member x.GetObservation(key, lookup) =
    let res = x.TryGetObservation(key, lookup) 
    if not res.HasValue then raise (KeyNotFoundException(key.ToString()))
    else res.Value

  ///
  /// [category:Accessors and slicing]
  member x.TryGet(key, lookup) =
    x.TryGetObservation(key, lookup) |> OptionalValue.map (fun (KeyValue(_, v)) -> v)

  ///
  /// [category:Accessors and slicing]
  member x.Get(key, lookup) =
    x.GetObservation(key, lookup).Value

  ///
  /// [category:Accessors and slicing]
  member x.GetByLevel(key:ICustomLookup<'K>) =
    let newIndex, levelCmd = indexBuilder.LookupLevel((index, Vectors.Return 0), key)
    let newVector = vectorBuilder.Build(levelCmd, [| vector |])
    Series(newIndex, newVector, vectorBuilder, indexBuilder)
    

  /// Attempts to get a value at the specified 'key'
  ///
  /// [category:Accessors and slicing]
  member x.TryGetObservation(key) = x.TryGetObservation(key, Lookup.Exact)
  ///
  /// [category:Accessors and slicing]
  member x.GetObservation(key) = x.GetObservation(key, Lookup.Exact)
  ///
  /// [category:Accessors and slicing]
  member x.TryGet(key) = x.TryGet(key, Lookup.Exact)
  ///
  /// [category:Accessors and slicing]
  member x.Get(key) = x.Get(key, Lookup.Exact)
  ///
  /// [category:Accessors and slicing]
  member x.TryGetAt(index) = 
    x.Vector.GetValue(Addressing.Int index)
  /// [category:Accessors and slicing]
  member x.GetKeyAt(index) = 
    x.Index.KeyAt(Addressing.Int index)
  /// [category:Accessors and slicing]
  member x.GetAt(index) = 
    x.TryGetAt(index).Value

  ///
  /// [category:Accessors and slicing]
  member x.Item with get(a) = x.Get(a)
  ///
  /// [category:Accessors and slicing]
  member x.Item with get(items) = x.GetItems items
  ///
  /// [category:Accessors and slicing]
  member x.Item with get(a) = x.GetByLevel(a)

  ///
  /// [category:Accessors and slicing]
  static member (?) (series:Series<_, _>, name:string) = series.Get(name, Lookup.Exact)

  // ----------------------------------------------------------------------------------------------
  // Projection and filtering
  // ----------------------------------------------------------------------------------------------

  /// [category:Projection and filtering]
  member x.Where(f:System.Func<KeyValuePair<'K, 'V>, bool>) = 
    let keys, optValues =
      [| for key, addr in index.Mappings do
          let opt = vector.GetValue(addr)
          let included = 
            // If a required value is missing, then skip over this
            opt.HasValue && f.Invoke (KeyValuePair(key, opt.Value)) 
          if included then yield key, opt  |]
      |> Array.unzip
    Series<_, _>
      ( indexBuilder.Create<_>(keys, None), vectorBuilder.CreateMissing(optValues),
        vectorBuilder, indexBuilder )

  /// [category:Projection and filtering]
  member x.WhereOptional(f:System.Func<KeyValuePair<'K, OptionalValue<'V>>, bool>) = 
    let keys, optValues =
      [| for key, addr in index.Mappings do
          let opt = vector.GetValue(addr)
          if f.Invoke (KeyValuePair(key, opt)) then yield key, opt |]
      |> Array.unzip
    Series<_, _>
      ( indexBuilder.Create<_>(keys, None), vectorBuilder.CreateMissing(optValues),
        vectorBuilder, indexBuilder )

  /// [category:Projection and filtering]
  member x.Select<'R>(f:System.Func<KeyValuePair<'K, 'V>, 'R>) = 
    let newVector =
      [| for key, addr in index.Mappings -> 
           vector.GetValue(addr) |> OptionalValue.bind (fun v -> 
             // If a required value is missing, then skip over this
             OptionalValue(f.Invoke(KeyValuePair(key, v))) ) |]
    let newIndex = indexBuilder.Project(index)
    Series<'K, 'R>(newIndex, vectorBuilder.CreateMissing(newVector), vectorBuilder, indexBuilder )

  /// [category:Projection and filtering]
  member x.SelectKeys<'R when 'R : equality>(f:System.Func<KeyValuePair<'K, OptionalValue<'V>>, 'R>) = 
    let newKeys =
      [| for key, addr in index.Mappings -> 
           f.Invoke(KeyValuePair(key, vector.GetValue(addr))) |]
    let newIndex = indexBuilder.Create(newKeys, None)
    Series<'R, _>(newIndex, vector, vectorBuilder, indexBuilder )

  /// [category:Projection and filtering]
  member x.SelectOptional<'R>(f:System.Func<KeyValuePair<'K, OptionalValue<'V>>, OptionalValue<'R>>) = 
    let newVector =
      index.Mappings |> Array.ofSeq |> Array.map (fun (key, addr) ->
           f.Invoke(KeyValuePair(key, vector.GetValue(addr))))
    let newIndex = indexBuilder.Project(index)
    Series<'K, 'R>(newIndex, vectorBuilder.CreateMissing(newVector), vectorBuilder, indexBuilder)

  // ----------------------------------------------------------------------------------------------
  // Appending, joining etc
  // ----------------------------------------------------------------------------------------------

  /// [category:Appending and joining]
  member series.Append(otherSeries:Series<'K, 'V>) =
    // Append the row indices and get transformation that combines two column vectors
    // (LeftOrRight - specifies that when column exist in both data frames then fail)
    let newIndex, cmd = 
      indexBuilder.Append( (index, Vectors.Return 0), (otherSeries.Index, Vectors.Return 1), 
                           VectorValueTransform.LeftOrRight )
    let newVector = vectorBuilder.Build(cmd, [| series.Vector; otherSeries.Vector |])
    Series(newIndex, newVector, vectorBuilder, indexBuilder)

 // TODO: Avoid duplicating code here and in Frame.Join

  /// [category:Appending and joining]
  member series.Join<'V2>(otherSeries:Series<'K, 'V2>) =
    series.Join(otherSeries, JoinKind.Outer, Lookup.Exact)

  /// [category:Appending and joining]
  member series.Join<'V2>(otherSeries:Series<'K, 'V2>, kind) =
    series.Join(otherSeries, kind, Lookup.Exact)

  /// [category:Appending and joining]
  member series.Join<'V2>(otherSeries:Series<'K, 'V2>, kind, lookup) =
    let restrictToThisIndex (restriction:IIndex<_>) (sourceIndex:IIndex<_>) vector = 
      if lookup = Lookup.Exact && restriction.Ordered && sourceIndex.Ordered then
        let min, max = index.KeyRange
        sourceIndex.Builder.GetRange(sourceIndex, Some(min, BoundaryBehavior.Inclusive), Some(max, BoundaryBehavior.Inclusive), vector)
      else sourceIndex, vector

    // Union row indices and get transformations to apply to left/right vectors
    let newIndex, thisRowCmd, otherRowCmd = 
      match kind with 
      | JoinKind.Inner ->
          indexBuilder.Intersect( (index, Vectors.Return 0), (otherSeries.Index, Vectors.Return 1) )
      | JoinKind.Left ->
          let otherRowIndex, vector = restrictToThisIndex index otherSeries.Index (Vectors.Return 1)
          let otherRowCmd = indexBuilder.Reindex(otherRowIndex, index, lookup, vector)
          index, Vectors.Return 0, otherRowCmd
      | JoinKind.Right ->
          let thisRowIndex, vector = restrictToThisIndex otherSeries.Index index (Vectors.Return 0)
          let thisRowCmd = indexBuilder.Reindex(thisRowIndex, otherSeries.Index, lookup, vector)
          otherSeries.Index, thisRowCmd, Vectors.Return 1
      | JoinKind.Outer | _ ->
          indexBuilder.Union( (index, Vectors.Return 0), (otherSeries.Index, Vectors.Return 1) )

    // ....
    let combine =
      VectorValueTransform.Create<Choice<'V, 'V2, 'V * 'V2>>(fun left right ->
        match left, right with 
        | OptionalValue.Present(Choice1Of3 l), OptionalValue.Present(Choice2Of3 r) -> 
            OptionalValue(Choice3Of3(l, r))
        | OptionalValue.Present(v), _
        | _, OptionalValue.Present(v) -> OptionalValue(v)
        | _ -> failwith "Series.Join: Unexpected vector structure")

    let inputThis : IVector<Choice<'V, 'V2, 'V * 'V2>> = vector.Select Choice1Of3 
    let inputThat : IVector<Choice<'V, 'V2, 'V * 'V2>> = otherSeries.Vector.Select Choice2Of3

    let combinedCmd = Vectors.Combine(thisRowCmd, otherRowCmd, combine)
    let newVector = vectorBuilder.Build(combinedCmd, [| inputThis; inputThat |])
    let newVector : IVector<_ opt * _ opt> = newVector.Select(function 
      | Choice3Of3(l, r) -> OptionalValue(l), OptionalValue(r)
      | Choice1Of3(l) -> OptionalValue(l), OptionalValue.Missing
      | Choice2Of3(r) -> OptionalValue.Missing, OptionalValue(r))
    Series(newIndex, newVector, vectorBuilder, indexBuilder)

  /// [category:Appending and joining]
  member series.JoinInner<'V2>(otherSeries:Series<'K, 'V2>) : Series<'K, 'V * 'V2> =
    let joined = series.Join(otherSeries, JoinKind.Inner, Lookup.Exact)
    joined.Select(fun (KeyValue(_, v)) ->
      match v with
      | OptionalValue.Present l, OptionalValue.Present r -> l, r 
      | _ -> failwith "JoinInner: Unexpected missing value")

  /// [category:Appending and joining]
  member series.Union(another:Series<'K, 'V>) = 
    series.Union(another, UnionBehavior.PreferLeft)
  
  /// [category:Appending and joining]
  member series.Union(another:Series<'K, 'V>, behavior) = 
    let newIndex, vec1, vec2 = indexBuilder.Union( (series.Index, Vectors.Return 0), (another.Index, Vectors.Return 1) )
    let transform = 
      match behavior with
      | UnionBehavior.PreferRight -> VectorHelpers.VectorValueTransform.RightIfAvailable
      | UnionBehavior.Exclusive -> VectorHelpers.VectorValueTransform.LeftOrRight
      | _ -> VectorHelpers.VectorValueTransform.LeftIfAvailable
    let vecCmd = Vectors.Combine(vec1, vec2, transform)
    let newVec = vectorBuilder.Build(vecCmd, [| series.Vector; another.Vector |])
    Series(newIndex, newVec, vectorBuilder, indexBuilder)

  // ----------------------------------------------------------------------------------------------
  // Resampling
  // ----------------------------------------------------------------------------------------------


  /// Resample the series based on a provided collection of keys. The values of the series
  /// are aggregated into chunks based on the specified keys. Depending on `direction`, the 
  /// specified key is either used as the smallest or as the greatest key of the chunk (with
  /// the exception of boundaries that are added to the first/last chunk).
  ///
  /// Such chunks are then aggregated using the provided `valueSelector` and `keySelector`
  /// (an overload that does not take `keySelector` just selects the explicitly provided key).
  ///
  /// ## Parameters
  ///  - `keys` - A collection of keys to be used for resampling of the series
  ///  - `direction` - If this parameter is `Direction.Forward`, then each key is
  ///    used as the smallest key in a chunk; for `Direction.Backward`, the keys are
  ///    used as the greatest keys in a chunk.
  ///  - `valueSelector` - A function that is used to collapse a generated chunk into a 
  ///    single value. Note that this function may be called with empty series.
  ///  - `keySelector` - A function that is used to generate a new key for each chunk.
  ///
  /// ## Remarks
  /// This operation is only supported on ordered series. The method throws
  /// `InvalidOperationException` when the series is not ordered.
  ///
  /// [category:Resampling]
  member x.Resample<'TNewKey, 'R when 'TNewKey : equality>(keys, direction, valueSelector:Func<_, _, _>, keySelector:Func<_, _, _>) =
    let newIndex, newVector = 
      indexBuilder.Resample
        ( x.Index, keys, direction, Vectors.Return 0, 
          (fun (key, (index, cmd)) -> 
              let window = Series<_, _>(index, vectorBuilder.Build(cmd, [| vector |]), vectorBuilder, indexBuilder)
              OptionalValue(valueSelector.Invoke(key, window))),
          (fun (key, (index, cmd)) -> 
              keySelector.Invoke(key, Series<_, _>(index, vectorBuilder.Build(cmd, [| vector |]), vectorBuilder, indexBuilder))) )
    Series<'TNewKey, 'R>(newIndex, newVector, vectorBuilder, indexBuilder)

  /// Resample the series based on a provided collection of keys. The values of the series
  /// are aggregated into chunks based on the specified keys. Depending on `direction`, the 
  /// specified key is either used as the smallest or as the greatest key of the chunk (with
  /// the exception of boundaries that are added to the first/last chunk).
  ///
  /// Such chunks are then aggregated using the provided `valueSelector` and `keySelector`
  /// (an overload that does not take `keySelector` just selects the explicitly provided key).
  ///
  /// ## Parameters
  ///  - `keys` - A collection of keys to be used for resampling of the series
  ///  - `direction` - If this parameter is `Direction.Forward`, then each key is
  ///    used as the smallest key in a chunk; for `Direction.Backward`, the keys are
  ///    used as the greatest keys in a chunk.
  ///  - `valueSelector` - A function that is used to collapse a generated chunk into a 
  ///    single value. Note that this function may be called with empty series.
  ///
  /// ## Remarks
  /// This operation is only supported on ordered series. The method throws
  /// `InvalidOperationException` when the series is not ordered.
  ///
  /// [category:Resampling]
  member x.Resample(keys, direction, valueSelector) =
    x.Resample(keys, direction, valueSelector, fun nk _ -> nk)

  /// Resample the series based on a provided collection of keys. The values of the series
  /// are aggregated into chunks based on the specified keys. Depending on `direction`, the 
  /// specified key is either used as the smallest or as the greatest key of the chunk (with
  /// the exception of boundaries that are added to the first/last chunk). The chunks
  /// are then returned as a nested series. 
  ///
  /// ## Parameters
  ///  - `keys` - A collection of keys to be used for resampling of the series
  ///  - `direction` - If this parameter is `Direction.Forward`, then each key is
  ///    used as the smallest key in a chunk; for `Direction.Backward`, the keys are
  ///    used as the greatest keys in a chunk.
  ///
  /// ## Remarks
  /// This operation is only supported on ordered series. The method throws
  /// `InvalidOperationException` when the series is not ordered.
  ///
  /// [category:Resampling]
  member x.Resample(keys, direction) =
    x.Resample(keys, direction, (fun k v -> v), fun nk _ -> nk)

  // ----------------------------------------------------------------------------------------------
  // Aggregation
  // ----------------------------------------------------------------------------------------------

  /// Returns a series containing the predecessor and an element for each input, except
  /// for the first one. The returned series is one key shorter (it does not contain a 
  /// value for the first key).
  ///
  /// ## Parameters
  ///  - `series` - The input series to be aggregated.
  ///
  /// ## Example
  ///
  ///     let input = series [ 1 => 'a'; 2 => 'b'; 3 => 'c']
  ///     let res = input.Pairwise()
  ///     res = series [2 => ('a', 'b'); 3 => ('b', 'c') ]
  ///
  /// [category:Windowing, chunking and grouping]
  member x.Pairwise() =
    x.Pairwise(Boundary.Skip)

  /// Returns a series containing an element and its neighbor for each input.
  /// The returned series is one key shorter (it does not contain a 
  /// value for the first or last key depending on `boundary`). If `boundary` is 
  /// other than `Boundary.Skip`, then the key is included in the returned series, 
  /// but its value is missing.
  ///
  /// ## Parameters
  ///  - `series` - The input series to be aggregated.
  ///  - `boundary` - Specifies the direction in which the series is aggregated and 
  ///    how the corner case is handled. If the value is `Boundary.AtEnding`, then the
  ///    function returns value and its successor, otherwise it returns value and its
  ///    predecessor.
  ///
  /// ## Example
  ///
  ///     let input = series [ 1 => 'a'; 2 => 'b'; 3 => 'c']
  ///     let res = input.Pairwise()
  ///     res = series [2 => ('a', 'b'); 3 => ('b', 'c') ]
  ///
  /// [category:Windowing, chunking and grouping]
  member x.Pairwise(boundary) =
    let dir = if boundary = Boundary.AtEnding then Direction.Forward else Direction.Backward
    let newIndex, newVector = 
      indexBuilder.Aggregate
        ( x.Index, WindowSize(2, boundary), Vectors.Return 0, 
          (fun (kind, (index, cmd)) -> 
              let actualVector = vectorBuilder.Build(cmd, [| vector |])
              let obs = [ for k, addr in index.Mappings -> actualVector.GetValue(addr) ]
              match obs with
              | [ OptionalValue.Present v1; OptionalValue.Present v2 ] -> 
                  OptionalValue( DataSegment(kind, (v1, v2)) )
              | [ _; _ ] -> OptionalValue.Missing
              | _ -> failwith "Pairwise: failed - expected two values" ),
          (fun (kind, (index, vector)) -> 
              if dir = Direction.Backward then index.Keys |> Seq.last
              else index.Keys |> Seq.head ) )
    Series<'K, DataSegment<'V * 'V>>(newIndex, newVector, vectorBuilder, indexBuilder)

  /// Aggregates an ordered series using the method specified by `Aggregation<K>` and then
  /// applies the provided `valueSelector` on each window or chunk to produce the result
  /// which is returned as a new series. A key for each window or chunk is
  /// selected using the specified `keySelector`.
  ///
  /// ## Parameters
  ///  - `aggregation` - Specifies the aggregation method using `Aggregation<K>`. This is
  ///    a discriminated union listing various chunking and windowing conditions.
  ///  - `keySelector` - A function that is called on each chunk to obtain a key.
  ///  - `valueSelector` - A value selector function that is called to aggregate each chunk or window.
  ///
  /// [category:Windowing, chunking and grouping]
  member x.Aggregate<'TNewKey, 'R when 'TNewKey : equality>(aggregation, keySelector:Func<_, _>, valueSelector:Func<_, _>) =
    let newIndex, newVector = 
      indexBuilder.Aggregate
        ( x.Index, aggregation, Vectors.Return 0, 
          (fun (kind, (index, cmd)) -> 
              let window = Series<_, _>(index, vectorBuilder.Build(cmd, [| vector |]), vectorBuilder, indexBuilder)
              OptionalValue(valueSelector.Invoke(DataSegment(kind, window)))),
          (fun (kind, (index, cmd)) -> 
              keySelector.Invoke(DataSegment(kind, Series<_, _>(index, vectorBuilder.Build(cmd, [| vector |]), vectorBuilder, indexBuilder)))) )
    Series<'TNewKey, 'R>(newIndex, newVector, vectorBuilder, indexBuilder)

  /// Groups a series (ordered or unordered) using the specified key selector (`keySelector`) 
  /// and then aggregates each group into a single value, returned in the resulting series,
  /// using the provided `valueSelector` function.
  ///
  /// ## Parameters
  ///  - `keySelector` - Generates a new key that is used for aggregation, based on the original 
  ///    key and value. The new key must support equality testing.
  ///  - `valueSelector` - A value selector function that is called to aggregate 
  ///    each group of collected elements.
  ///
  /// [category:Windowing, chunking and grouping]
  member x.GroupBy(keySelector, valueSelector) =
    let newIndex, newVector = 
      indexBuilder.GroupBy
        ( x.Index, 
          (fun key -> 
              x.TryGet(key) |> OptionalValue.map (keySelector key)), Vectors.Return 0, 
          (fun (newKey, (index, cmd)) -> 
              let group = Series<_, _>(index, vectorBuilder.Build(cmd, [| vector |]), vectorBuilder, indexBuilder)
              valueSelector newKey group ) )
    Series<'TNewKey, 'R>(newIndex, newVector, vectorBuilder, indexBuilder)

  // ----------------------------------------------------------------------------------------------
  // Indexing
  // ----------------------------------------------------------------------------------------------

  /// [category:Indexing]
  member x.Realign(newKeys) = 
    let ns = 
      Series( Index.ofKeys newKeys, vectorBuilder.Create (Array.ofSeq newKeys),
              vectorBuilder, indexBuilder )
    ns.Join(x, JoinKind.Left).SelectOptional(fun kvp ->
      match kvp with
      | KeyValue(k, OptionalValue.Present(_, v)) -> v
      | _ -> OptionalValue.Missing )

  /// [category:Indexing]
  member x.IndexOrdinally() = 
    let newIndex = indexBuilder.Create(x.Index.Keys |> Seq.mapi (fun i _ -> i), Some true)
    Series<int, _>(newIndex, vector, vectorBuilder, indexBuilder)

  /// [category:Indexing]
  member x.IndexWith(keys) = 
    let newIndex = indexBuilder.Create(keys, None)
    Series<'TNewKey, _>(newIndex, vector, vectorBuilder, indexBuilder)


  // ----------------------------------------------------------------------------------------------
  // Operators and F# functions
  // ----------------------------------------------------------------------------------------------

  static member inline internal NullaryGenericOperation<'K, 'T1, 'T2>(series:Series<'K, 'T1>, op : 'T1 -> 'T2) = 
    series.Select(fun (KeyValue(k, v)) -> op v)
  static member inline internal NullaryOperation<'T>(series:Series<'K, 'T>, op : 'T -> 'T) = 
    series.Select(fun (KeyValue(k, v)) -> op v)
  static member inline internal ScalarOperationL<'T>(series:Series<'K, 'T>, scalar, op : 'T -> 'T -> 'T) = 
    series.Select(fun (KeyValue(k, v)) -> op v scalar)
  static member inline internal ScalarOperationR<'T>(scalar, series:Series<'K, 'T>, op : 'T -> 'T -> 'T) = 
    series.Select(fun (KeyValue(k, v)) -> op scalar v)

  static member inline internal VectorOperation<'T>(series1:Series<'K, 'T>, series2:Series<'K, 'T>, op) : Series<_, 'T> =
    let joined = series1.Join(series2)
    joined.SelectOptional(fun (KeyValue(_, v)) -> 
      match v with
      | OptionalValue.Present(OptionalValue.Present a, OptionalValue.Present b) -> 
          OptionalValue(op a b)
      | _ -> OptionalValue.Missing )


  /// [category:Operators]
  static member (+) (scalar, series) = Series<'K, _>.ScalarOperationR<int>(scalar, series, (+))
  /// [category:Operators]
  static member (+) (series, scalar) = Series<'K, _>.ScalarOperationL<int>(series, scalar, (+))
  /// [category:Operators]
  static member (-) (scalar, series) = Series<'K, _>.ScalarOperationR<int>(scalar, series, (-))
  /// [category:Operators]
  static member (-) (series, scalar) = Series<'K, _>.ScalarOperationL<int>(series, scalar, (-))
  /// [category:Operators]
  static member (*) (scalar, series) = Series<'K, _>.ScalarOperationR<int>(scalar, series, (*))
  /// [category:Operators]
  static member (*) (series, scalar) = Series<'K, _>.ScalarOperationL<int>(series, scalar, (*))
  /// [category:Operators]
  static member (/) (scalar, series) = Series<'K, _>.ScalarOperationR<int>(scalar, series, (/))
  /// [category:Operators]
  static member (/) (series, scalar) = Series<'K, _>.ScalarOperationL<int>(series, scalar, (/))

  /// [category:Operators]
  static member (+) (scalar, series) = Series<'K, _>.ScalarOperationR<float>(scalar, series, (+))
  /// [category:Operators]
  static member (+) (series, scalar) = Series<'K, _>.ScalarOperationL<float>(series, scalar, (+))
  /// [category:Operators]
  static member (-) (scalar, series) = Series<'K, _>.ScalarOperationR<float>(scalar, series, (-))
  /// [category:Operators]
  static member (-) (series, scalar) = Series<'K, _>.ScalarOperationL<float>(series, scalar, (-))
  /// [category:Operators]
  static member (*) (scalar, series) = Series<'K, _>.ScalarOperationR<float>(scalar, series, (*))
  /// [category:Operators]
  static member (*) (series, scalar) = Series<'K, _>.ScalarOperationL<float>(series, scalar, (*))
  /// [category:Operators]
  static member (/) (scalar, series) = Series<'K, _>.ScalarOperationR<float>(scalar, series, (/))
  /// [category:Operators]
  static member (/) (series, scalar) = Series<'K, _>.ScalarOperationL<float>(series, scalar, (/))
  /// [category:Operators]
  static member Pow (scalar, series) = Series<'K, _>.ScalarOperationR<float>(scalar, series, ( ** ))
  /// [category:Operators]
  static member Pow (series, scalar) = Series<'K, _>.ScalarOperationL<float>(series, scalar, ( ** ))

  /// [category:Operators]
  static member (+) (s1, s2) = Series<'K, _>.VectorOperation<int>(s1, s2, (+))
  /// [category:Operators]
  static member (-) (s1, s2) = Series<'K, _>.VectorOperation<int>(s1, s2, (-))
  /// [category:Operators]
  static member (*) (s1, s2) = Series<'K, _>.VectorOperation<int>(s1, s2, (*))
  /// [category:Operators]
  static member (/) (s1, s2) = Series<'K, _>.VectorOperation<int>(s1, s2, (/))

  /// [category:Operators]
  static member (+) (s1, s2) = Series<'K, _>.VectorOperation<float>(s1, s2, (+))
  /// [category:Operators]
  static member (-) (s1, s2) = Series<'K, _>.VectorOperation<float>(s1, s2, (-))
  /// [category:Operators]
  static member (*) (s1, s2) = Series<'K, _>.VectorOperation<float>(s1, s2, (*))
  /// [category:Operators]
  static member (/) (s1, s2) = Series<'K, _>.VectorOperation<float>(s1, s2, (/))
  /// [category:Operators]
  static member Pow(s1, s2) = Series<'K, _>.VectorOperation<float>(s1, s2, ( ** ))

  // Trigonometric
  
  /// [category:Operators]
  static member Acos(series) = Series<'K, _>.NullaryOperation<float>(series, acos)
  /// [category:Operators]
  static member Asin(series) = Series<'K, _>.NullaryOperation<float>(series, asin)
  /// [category:Operators]
  static member Atan(series) = Series<'K, _>.NullaryOperation<float>(series, atan)
  /// [category:Operators]
  static member Sin(series) = Series<'K, _>.NullaryOperation<float>(series, sin)
  /// [category:Operators]
  static member Sinh(series) = Series<'K, _>.NullaryOperation<float>(series, sinh)
  /// [category:Operators]
  static member Cos(series) = Series<'K, _>.NullaryOperation<float>(series, cos)
  /// [category:Operators]
  static member Cosh(series) = Series<'K, _>.NullaryOperation<float>(series, cosh)
  /// [category:Operators]
  static member Tan(series) = Series<'K, _>.NullaryOperation<float>(series, tan)
  /// [category:Operators]
  static member Tanh(series) = Series<'K, _>.NullaryOperation<float>(series, tanh)

  // Actually useful

  /// [category:Operators]
  static member Abs(series) = Series<'K, _>.NullaryOperation<float>(series, abs)
  /// [category:Operators]
  static member Abs(series) = Series<'K, _>.NullaryOperation<int>(series, abs)
  /// [category:Operators]
  static member Ceiling(series) = Series<'K, _>.NullaryOperation<float>(series, ceil)
  /// [category:Operators]
  static member Exp(series) = Series<'K, _>.NullaryOperation<float>(series, exp)
  /// [category:Operators]
  static member Floor(series) = Series<'K, _>.NullaryOperation<float>(series, floor)
  /// [category:Operators]
  static member Truncate(series) = Series<'K, _>.NullaryOperation<float>(series, truncate)
  /// [category:Operators]
  static member Log(series) = Series<'K, _>.NullaryOperation<float>(series, log)
  /// [category:Operators]
  static member Log10(series) = Series<'K, _>.NullaryOperation<float>(series, log10)
  /// [category:Operators]
  static member Round(series) = Series<'K, _>.NullaryOperation<float>(series, round)
  /// [category:Operators]
  static member Sign(series) = Series<'K, _>.NullaryGenericOperation<_, float, _>(series, sign)
  /// [category:Operators]
  static member Sqrt(series) = Series<'K, _>.NullaryGenericOperation<_, float, _>(series, sqrt)

  // ----------------------------------------------------------------------------------------------
  // Overrides & interfaces
  // ----------------------------------------------------------------------------------------------

  override series.Equals(another) = 
    match another with
    | null -> false
    | :? Series<'K, 'V> as another -> 
        series.Index.Equals(another.Index) && series.Vector.Equals(another.Vector)
    | _ -> false

  override series.GetHashCode() =
    let combine h1 h2 = ((h1 <<< 5) + h1) ^^^ h2
    combine (series.Index.GetHashCode()) (series.Vector.GetHashCode())

  interface ISeries<'K> with
    member x.TryGetObject(k) = this.TryGet(k) |> OptionalValue.map box
    member x.Vector = vector :> IVector
    member x.Index = index

  override series.ToString() =
    if vector.SuppressPrinting then "(Suppressed)" else
      seq { for item in series.Observations |> Seq.startAndEnd Formatting.StartInlineItemCount Formatting.EndInlineItemCount ->
              match item with 
              | Choice2Of3() -> " ... "
              | Choice1Of3(KeyValue(k, v)) | Choice3Of3(KeyValue(k, v)) -> sprintf "%O => %O" k v }
      |> String.concat "; "
      |> sprintf "series [ %s]" 

  interface IFsiFormattable with
    member series.Format() = 
      let getLevel ordered previous reset maxLevel level (key:'K) = 
        let levelKey = 
          if level = 0 && maxLevel = 0 then box key
          else CustomKey.Get(key).GetLevel(level)
        if ordered && (Some levelKey = !previous) then "" 
        else previous := Some levelKey; reset(); levelKey.ToString()

      if vector.SuppressPrinting then "(Suppressed)" else
        let key = series.Index.Keys |> Seq.headOrNone
        match key with 
        | None -> "(Empty)"
        | Some key ->
            let levels = CustomKey.Get(key).Levels
            let previous = Array.init levels (fun _ -> ref None)
            let reset i () = for j in i + 1 .. levels - 1 do previous.[j] := None
            seq { for item in index.Mappings |> Seq.startAndEnd Formatting.StartItemCount Formatting.EndItemCount  do
                    match item with 
                    | Choice1Of3(k, a) | Choice3Of3(k, a) -> 
                        let v = vector.GetValue(a)
                        yield [ 
                          // Yield all row keys
                          for level in 0 .. levels - 1 do 
                            yield getLevel series.Index.Ordered previous.[level] (reset level) levels level k
                          yield "->"
                          yield v.ToString() ]
                    | Choice2Of3() -> 
                        yield [ 
                          yield "..."
                          for level in 1 .. levels - 1 do yield ""
                          yield "->"
                          yield "..." ] }
            |> array2D
            |> Formatting.formatTable

  // ----------------------------------------------------------------------------------------------
  // Nicer constructor
  // ----------------------------------------------------------------------------------------------

  new(pairs:seq<KeyValuePair<'K, 'V>>) =
    Series(pairs |> Seq.map (fun kvp -> kvp.Key), pairs |> Seq.map (fun kvp -> kvp.Value))

  new(keys:seq<_>, values:seq<_>) = 
    let vectorBuilder = Vectors.ArrayVector.ArrayVectorBuilder.Instance
    let indexBuilder = Indices.Linear.LinearIndexBuilder.Instance
    Series( Index.ofKeys keys, vectorBuilder.Create (Array.ofSeq values),
            vectorBuilder, indexBuilder )

// ------------------------------------------------------------------------------------------------
// Untyped series
// ------------------------------------------------------------------------------------------------

type ObjectSeries<'K when 'K : equality> internal(index:IIndex<_>, vector, vectorBuilder, indexBuilder) = 
  inherit Series<'K, obj>(index, vector, vectorBuilder, indexBuilder)
  
  member x.GetAs<'R>(column) : 'R = 
    System.Convert.ChangeType(x.Get(column), typeof<'R>) |> unbox
  member x.TryGetAs<'R>(column) : OptionalValue<'R> = 
    x.TryGet(column) |> OptionalValue.map (fun v -> System.Convert.ChangeType(v, typeof<'R>) |> unbox)
  static member (?) (series:ObjectSeries<_>, name:string) = series.GetAs<float>(name)

  member x.TryAs<'R>(strict) =
    match box vector with
    | :? IVector<'R> as vec -> 
        let newIndex = indexBuilder.Project(index)
        OptionalValue(Series(newIndex, vec, vectorBuilder, indexBuilder))
    | _ -> 
        ( if strict then VectorHelpers.tryCastType vector
          else VectorHelpers.tryChangeType vector )
        |> OptionalValue.map (fun vec -> 
          let newIndex = indexBuilder.Project(index)
          Series(newIndex, vec, vectorBuilder, indexBuilder))

  member x.TryAs<'R>() =
    x.TryAs<'R>(false)

  member x.As<'R>() =
    let newIndex = indexBuilder.Project(index)
    match box vector with
    | :? IVector<'R> as vec -> Series(newIndex, vec, vectorBuilder, indexBuilder)
    | _ -> Series(newIndex, VectorHelpers.changeType vector, vectorBuilder, indexBuilder)
