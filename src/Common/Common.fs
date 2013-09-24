﻿namespace FSharp.DataFrame

open System

// --------------------------------------------------------------------------------------
// OptionalValue<T> type
// --------------------------------------------------------------------------------------

/// Value type that represents a potentially missing value. This is similar to 
/// `System.Nullable<T>`, but does not restrict the contained value to be a value
/// type, so it can be used for storing values of any types. When obtained from
/// `DataFrame<R, C>` or `Series<K, T>`, the `Value` will never be `Double.NaN` or `null`
/// (but this is not, in general, checked when constructing the value).
///
/// The type is only used in C#-friendly API. F# operations generally use expose
/// standard F# `option<T>` type instead. However, there the `OptionalValue` module
/// contains helper functions for using this type from F# as well as `Missing` and
/// `Present` active patterns.
[<Struct>]
type OptionalValue<'T> private (hasValue:bool, value:'T) = 
  /// Gets a value indicating whether the current `OptionalValue<T>` has a value
  member x.HasValue = hasValue

  /// Returns the value stored in the current `OptionalValue<T>`. 
  /// Exceptions:
  ///   `InvalidOperationException` - Thrown when `HasValue` is `false`.
  member x.Value = 
    if hasValue then value
    else invalidOp "OptionalValue.Value: Value is not available" 
  
  /// Returns the value stored in the current `OptionalValue<T>` or 
  /// the default value of the type `T` when a value is not present.
  member x.ValueOrDefault = value

  /// Creates a new instance of `OptionalValue<T>` that contains  
  /// the specified `T` value .
  new (value:'T) = OptionalValue(true, value)

  /// Returns a new instance of `OptionalValue<T>` that does not contain a value.
  static member Missing = OptionalValue(false, Unchecked.defaultof<'T>)

  /// Prints the value or "<null>" when the value is present, but is `null`
  /// or "<missing>" when the value is not present (`HasValue = false`).
  override x.ToString() = 
    if hasValue then 
      if Object.Equals(null, value) then "<null>"
      else value.ToString() 
    else "<missing>"

/// Specifies in which direction should we look when performing operations such as
/// `Series.Pairwise`. For example consider:
///
///     let abc = Series.ofObservations [ 1 => "a"; 2 => "b"; 3 => "c" ]
///
///     // When looking forward, observations have key of the first element
///     abc.Pairwise(direction=Direction.Forward) = 
///       Series.ofObservations [ 1 => ("a", "b"); 2 => ("b", "c") ]
///
///     // When looking backward, observations have key of the second element
///     abc.Pairwise(direction=Direction.Backward) = 
///       Series.ofObservations [ 2 => ("a", "b"); 3 => ("b", "c") ]
///
type Direction = 
  | Backward = 0
  | Forward = 1 

/// Represents boundary behaviour for operations such as floating window. The type
/// specifies whether incomplete windows (of smaller than required length) should be
/// produced at the beginning (`AtBeginning`) or at the end (`AtEnding`) or
/// skipped (`Skip`). For chunking, combinations are allowed too - to skip incomplete
/// chunk at the beginning, use `Boundary.Skip ||| Boundary.AtBeginning`.
[<Flags>]
type Boundary =
  | AtBeginning = 1
  | AtEnding = 2
  | Skip = 4

/// Represents a kind of `DataSegment<T>`. See that type for more information.
type DataSegmentKind = Complete | Incomplete

/// Represents a segment of a series or sequence. The value is returned from 
/// various functions that aggregate data into chunks or floating windows. The 
/// `Complete` case represents complete segment (e.g. of the specified size) and
/// `Boundary` represents segment at the boundary (e.g. smaller than the required
/// size). For example (using internal `windowed` function):
///
//      open FSharp.DataFrame.Internal
///
///     Seq.windowedWithBounds 3 Boundary.AtBeginning [ 1; 2; 3; 4 ] |> Array.ofSeq = 
///       [| DataSegment(Incomplete, [| 1 |])
///          DataSegment(Incomplete, [| 1; 2 |])
///          DataSegment(Complete [| 1; 2; 3 |])
///          DataSegment(Complete [| 2; 3; 4 |]) |]
///
/// If you do not need to distinguish the two cases, you can use the `Data` property
/// to get the array representing the segment data.
type DataSegment<'T> = 
  | DataSegment of DataSegmentKind * 'T
  /// Returns the data associated with the segment
  /// (for boundary segment, this may be smaller than the required window size)
  member x.Data = let (DataSegment(_, data)) = x in data
  /// Return the kind of this segment
  member x.Kind = let (DataSegment(kind, _)) = x in kind


/// Provides helper functions and active patterns for working with `DataSegment` values
module DataSegment = 
  /// A complete active pattern that extracts the kind and data from a `DataSegment`
  /// value. This makes it easier to write functions that only need data:
  ///
  ///    let sumAny = function DataSegment.Any(_, data) -> Series.sum data
  ///
  let (|Any|) (ds:DataSegment<'T>) = ds.Kind, ds.Data
  
  /// Complete active pattern that makes it possible to write functions that behave 
  /// differently for complete and incomplete segments. For example, the following 
  /// returns zero for incomplete segments:
  ///
  ///     let sumSegmentOrZero = function
  ///       | DataSegment.Complete(value) -> Series.sum value
  ///       | DataSegment.Incomplete _ -> 0.0
  ///
  let (|Complete|Incomplete|) (ds:DataSegment<_>) =
    if ds.Kind = DataSegmentKind.Complete then Complete(ds.Data)
    else Incomplete(ds.Data)

  /// Returns the data property of the specified `DataSegment<T>`
  [<CompiledName("GetData")>]
  let data (ds:DataSegment<_>) = ds.Data

  /// Returns the kind property of the specified `DataSegment<T>`
  [<CompiledName("GetKind")>]
  let kind (ds:DataSegment<_>) = ds.Kind


// --------------------------------------------------------------------------------------
// OptionalValue module (to be used from F#)
// --------------------------------------------------------------------------------------

/// Provides various helper functions for using the `OptionalValue<T>` type from F#
/// (The functions are similar to those in the standard `Option` module).
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module OptionalValue = 

  /// If the `OptionalValue<T>` does not contain a value, then returns a new 
  /// `OptionalValue<R>.Empty`. Otherwise, returns the result of applying the 
  /// function `f` to the value contained in the provided optional value.
  [<CompiledName("Bind")>]
  let inline bind f (input:OptionalValue<'T>) : OptionalValue<'R> = 
    if input.HasValue then f input.Value
    else OptionalValue.Missing

  /// If the `OptionalValue<T>` does not contain a value, then returns a new 
  /// `OptionalValue<R>.Empty`. Otherwise, returns the result `OptionalValue<R>`
  /// containing the result of applying the function `f` to the value contained 
  /// in the provided optional value.
  [<CompiledName("Map")>]
  let inline map f (input:OptionalValue<'T>) : OptionalValue<'R> = 
    if input.HasValue then OptionalValue(f input.Value)
    else OptionalValue.Missing

  /// Creates `OptionalValue<T>` from a tuple of type `bool * 'T`. This function
  /// can be used with .NET methods that use `out` arguments. For example:
  ///
  ///     Int32.TryParse("42") |> OptionalValue.ofTuple
  ///
  [<CompiledName("OfTuple")>]
  let inline ofTuple (b, value:'T) =
    if b then OptionalValue(value) else OptionalValue.Missing

  /// Creates `OptionalValue<T>` from a .NET `Nullable<T>` type.
  [<CompiledName("OfNullable")>]
  let inline ofNullable (value:Nullable<'T>) =
    if value.HasValue then OptionalValue(value.Value) else OptionalValue.Missing

  /// Turns the `OptionalValue<T>` into a corresponding standard F# `option<T>` value
  let inline asOption (value:OptionalValue<'T>) = 
    if value.HasValue then Some value.Value else None

  /// Turns a standard F# `option<T>` value into a corresponding `OptionalValue<T>`
  let inline ofOption (opt:option<'T>) = 
    match opt with
    | None -> OptionalValue.Missing
    | Some v -> OptionalValue(v)

  /// Complete active pattern that can be used to pattern match on `OptionalValue<T>`.
  /// For example:
  ///
  ///     let optVal = OptionalValue(42)
  ///     match optVal with
  ///     | OptionalValue.Missing -> printfn "Empty"
  ///     | OptionalValue.Present(v) -> printfn "Contains %d" v
  ///
  let (|Missing|Present|) (optional:OptionalValue<'T>) =
    if optional.HasValue then Present(optional.Value)
    else Missing


// --------------------------------------------------------------------------------------
// Internals - working with missing values   
// --------------------------------------------------------------------------------------

namespace FSharp.DataFrame.Internal

open System
open System.Linq
open System.Drawing
open FSharp.DataFrame
open System.Collections.Generic

/// Utility functions for identifying missing values. The `isNA` function 
/// can be used to test whether a value represents a missing value - this includes
/// the `null` value, `Nullable<T>` value with `HasValue = false` and 
/// `Single.NaN` as well as `Double.NaN`.
///
/// The functions in this module are not intended to be called directly.
module MissingValues =

  // TODO: Possibly optimize (in some cases) using static member constraints?

  let isNA<'T> () =
    let ty = typeof<'T>
    let isNullable = ty.IsGenericType && (ty.GetGenericTypeDefinition() = typedefof<Nullable<_>>)
    let nanTest : 'T -> bool =
      if ty = typeof<float> then unbox Double.IsNaN
      elif ty = typeof<float32> then unbox Single.IsNaN
      elif ty.IsValueType && not isNullable then (fun _ -> false)
      else (fun v -> Object.Equals(null, box v))
    nanTest

  let inline containsNA (data:'T[]) = 
    let isNA = isNA<'T>() 
    Array.exists isNA data

  let inline containsMissingOrNA (data:OptionalValue<'T>[]) = 
    let isNA = isNA<'T>() 
    data |> Array.exists (fun v -> not v.HasValue || isNA v.Value)

  let inline createNAArray (data:'T[]) =   
    let isNA = isNA<'T>() 
    data |> Array.map (fun v -> if isNA v then OptionalValue.Missing else OptionalValue(v))

  let inline createMissingOrNAArray (data:OptionalValue<'T>[]) =   
    let isNA = isNA<'T>() 
    data |> Array.map (fun v -> 
      if not v.HasValue || isNA v.Value then OptionalValue.Missing else OptionalValue(v.Value))


// --------------------------------------------------------------------------------------
// Internals - various functions for working with collections
// --------------------------------------------------------------------------------------

/// Provides helper functions for working with `IReadOnlyList<T>` similar to those 
/// in the `Array` module. Most importantly, F# 3.0 does not know that array implements
/// `IReadOnlyList<T>`, so the `ofArray` function performs boxing & unboxing to convert.
module IReadOnlyList =
  /// Converts an array to IReadOnlyList. In F# 3.0, the language does not
  /// know that array implements IReadOnlyList, so this is just boxing/unboxing.
  let inline ofArray (array:'T[]) : IReadOnlyList<'T> = unbox (box array)

  /// Converts a lazy sequence to fully evaluated IReadOnlyList
  let inline ofSeq (array:seq<'T>) : IReadOnlyList<'T> = unbox (box (Array.ofSeq array))
  
  /// Sum elements of the IReadOnlyList
  let inline sum (list:IReadOnlyList<'T>) = 
    let mutable total = LanguagePrimitives.GenericZero
    for i in 0 .. list.Count - 1 do total <- total + list.[i]
    total

  /// Sum elements of the IReadOnlyList
  let inline average (list:IReadOnlyList<'T>) = 
    let mutable total = LanguagePrimitives.GenericZero
    for i in 0 .. list.Count - 1 do total <- total + list.[i]
    LanguagePrimitives.DivideByInt total list.Count

  /// Sum elements of the IReadOnlyList
  let inline sumOptional (list:IReadOnlyList<OptionalValue<'T>>) = 
    let mutable total = LanguagePrimitives.GenericZero
    for i in 0 .. list.Count - 1 do 
      if list.[i].HasValue then total <- total + list.[i].Value
    total

  /// Sum elements of the IReadOnlyList
  let inline averageOptional (list:IReadOnlyList<OptionalValue<'T>>) = 
    let mutable total = LanguagePrimitives.GenericZero
    let mutable count = 0 
    for i in 0 .. list.Count - 1 do 
      if list.[i].HasValue then 
        total <- total + list.[i].Value
        count <- count + 1
    LanguagePrimitives.DivideByInt total count


/// This module contains additional functions for working with arrays. 
/// `FSharp.DataFrame.Internals` is opened, it extends the standard `Array` module.
module Array = 
  /// Drop a specified range from a given array. The operation is inclusive on
  /// both sides. Given [ 1; 2; 3; 4 ] and indices (1, 2), the result is [ 1; 4 ]
  let inline dropRange first last (data:'T[]) =
    if last < first then invalidOp "The first index must be smaller than or equal to the last."
    if first < 0 || last >= data.Length then invalidArg "first" "The index must be within the array range."
    Array.append (data.[.. first - 1]) (data.[last + 1 ..])

  let inline private binarySearch key (comparer:System.Collections.Generic.IComparer<'T>) (array:'T[]) =
    let rec search (lo, hi) =
      if lo = hi then lo else
      let mid = (lo + hi) / 2
      match comparer.Compare(key, array.[mid]) with 
      | 0 -> mid
      | n when n < 0 -> search (lo, max lo (mid - 1))
      | _ -> search (min hi (mid + 1), hi) 
    search (0, array.Length - 1) 

  /// Returns the index of 'key' or the index of immediately following value.
  /// If the specified key is greater than all keys in the array, None is returned.
  let binarySearchNearestGreater key (comparer:System.Collections.Generic.IComparer<'T>) (array:'T[]) =
    if array.Length = 0 then None else
    let loc = binarySearch key comparer array
    if comparer.Compare(array.[loc], key) >= 0 then Some loc
    elif loc + 1 < array.Length && comparer.Compare(array.[loc + 1], key) >= 1 then Some (loc + 1)
    else None

  /// Returns the index of 'key' or the index of immediately preceeding value.
  /// If the specified key is smaller than all keys in the array, None is returned.
  let binarySearchNearestSmaller key (comparer:System.Collections.Generic.IComparer<'T>) (array:'T[]) =
    if array.Length = 0 then None else
    let loc = binarySearch key comparer array
    if comparer.Compare(array.[loc], key) <= 0 then Some loc
    elif loc - 1 >= 0 && comparer.Compare(array.[loc - 1], key) <= 0 then Some (loc - 1)
    else None


/// This module contains additional functions for working with sequences. 
/// `FSharp.DataFrame.Internals` is opened, it extends the standard `Seq` module.
module Seq = 

  /// Comapre two sequences using the `Equals` method. Returns true
  /// when all their elements are equal and they have the same size.
  let structuralEquals (s1:seq<'T>) (s2:seq<'T>) = 
    let mutable result = None
    use en1 = s1.GetEnumerator()
    use en2 = s2.GetEnumerator()
    while result.IsNone do 
      let canNext1, canNext2 = en1.MoveNext(), en2.MoveNext()
      if canNext1 <> canNext2 then result <- Some false
      elif not canNext1 then result <- Some true
      elif not ((box en1.Current).Equals(en2.Current)) then result <- Some false
    result.Value

  /// Calculate hash code of a sequence, based on the values
  let structuralHash (s:seq<'T>) = 
    let combine h1 h2 = ((h1 <<< 5) + h1) ^^^ h2
    s |> Seq.map (fun v -> (box v).GetHashCode()) |> Seq.fold combine -1

  /// If the input is non empty, returns `Some(head)` where `head` is 
  /// the first value. Otherwise, returns `None`.
  let headOrNone (input:seq<_>) = 
    (input |> Seq.map Some).FirstOrDefault()

  /// Returns the specified number of elements from the end of the sequence
  /// Note that this needs to store the specified number of elements in memory
  /// and it needs to iterate over the entire sequence.
  let lastFew count (input:seq<_>) = 
    let cache = Array.zeroCreate count 
    let mutable cacheCount = 0
    let mutable cacheIndex = 0
    for v in input do 
      cache.[cacheIndex] <- v
      cacheCount <- cacheCount + 1
      cacheIndex <- (cacheIndex + 1) % count
    let available = min cacheCount count
    cacheIndex <- (cacheIndex - available + count) % count
    let cacheIndex = cacheIndex
    seq { for i in 0 .. available - 1 do yield cache.[(cacheIndex + i) % count] }
    
  // lastFew 3 List.empty<int> |> List.ofSeq = []
  // lastFew 3 [ 1 .. 10 ]  |> List.ofSeq = [ 8; 9; 10]

  /// Calls the `GetEnumerator` method. Simple function to guide type inference.
  let getEnumerator (s:seq<_>) = s.GetEnumerator()

  /// Given a sequence, returns `startCount` number of elements at the beginning 
  /// of the sequence (wrapped in `Choice1Of3`) followed by one `Choice2Of2()` value
  /// and then followed by `endCount` number of elements at the end of the sequence
  /// wrapped in `Choice3Of3`. If the input is shorter than `startCount + endCount`,
  /// then all values are returned and wrapped in `Choice1Of3`.
  let startAndEnd startCount endCount input = seq { 
    let lastItems = Array.zeroCreate endCount
    let lastPointer = ref 0
    let written = ref 0
    let skippedAny = ref false
    let writeNext(v) = 
      if !written < endCount then incr written; 
      lastItems.[!lastPointer] <- v; lastPointer := (!lastPointer + 1) % endCount
    let readNext() = let p = !lastPointer in lastPointer := (!lastPointer + 1) % endCount; lastItems.[p]
    let readRest() = 
      lastPointer := (!lastPointer + endCount - !written) % endCount
      seq { for i in 1 .. !written -> readNext() }

    use en = getEnumerator input 
    let rec skipToEnd() = 
      if en.MoveNext() then 
        writeNext(en.Current)
        skippedAny := true
        skipToEnd()
      else seq { if skippedAny.Value then 
                   yield Choice2Of3()
                   for v in readRest() -> Choice3Of3 v 
                 else for v in readRest() -> Choice1Of3 v }
    let rec fillRest count = 
      if count = endCount then skipToEnd()
      elif en.MoveNext() then 
        writeNext(en.Current)
        fillRest (count + 1)
      else seq { for v in readRest() -> Choice1Of3 v }
    let rec yieldFirst count = seq { 
      if count = 0 then yield! fillRest 0
      elif en.MoveNext() then 
        yield Choice1Of3 en.Current
        yield! yieldFirst (count - 1) }
    yield! yieldFirst startCount }


  /// Generate floating windows from the input sequence. New floating window is 
  /// started for each element. To find the end of the window, the function calls
  /// the provided argument `f` with the first and the last elements of the window
  /// as arguments. A window ends when `f` returns `false`.
  let windowedWhile (f:'T -> 'T -> bool) input = seq {
    let windows = System.Collections.Generic.LinkedList()
    for v in input do
      windows.AddLast( (v, []) ) |> ignore
      // Walk over all windows; use 'f' to determine if the item
      // should be added - if so, add it, otherwise yield window
      let win = ref windows.First
      while win.Value <> null do 
        let start, items = win.Value.Value
        let next = win.Value.Next
        if f start v then win.Value.Value <- start, v::items
        else 
          yield items |> List.rev |> Array.ofList
          windows.Remove(win.Value)
        win := next
    for _, win in windows do
      yield win |> List.rev |> Array.ofList }

  
  /// Generate non-verlapping chunks from the input sequence. A chunk is started 
  /// at the beginning and then immediately after the end of the previous chunk.
  /// To find the end of the chunk, the function calls the provided argument `f` 
  /// with the first and the last elements of the chunk as arguments. A chunk 
  /// ends when `f` returns `false`.
  let chunkedWhile f input = seq {
    let chunk = ref None
    for v in input do
      match chunk.Value with 
      | None -> chunk := Some(v, [v])
      | Some(start, items) ->
          if f start v then chunk := Some(start, v::items)
          else
            yield items |> List.rev |> Array.ofList
            chunk := Some(v, [v])
    match chunk.Value with
    | Some (_, items) -> yield items |> List.rev |> Array.ofList
    | _ -> () }


  /// A version of `Seq.windowed` that allows specifying more complex boundary
  /// behaviour. The `boundary` argument can specify one of the following options:
  /// 
  ///  * `Boundary.Skip` - only full windows are returned (like `Seq.windowed`)
  ///  * `Boundary.AtBeginning` - incomplete windows (smaller than the required
  ///    size) are returned at the beginning.
  ///  * `Boundary.AtEnding` - incomplete windows are returned at the end.
  ///
  /// The result is a sequence of `DataSegnebt<T>` values, which makes it 
  /// easy to distinguish between complete and incomplete windows.
  let windowedWithBounds size boundary (input:seq<'T>) = seq {
    let windows = Array.create size []
    let currentWindow = ref 0
    for v in input do
      for i in 0 .. windows.Length - 1 do windows.[i] <- v::windows.[i]
      let win = windows.[currentWindow.Value] |> Array.ofList |> Array.rev
      // If the window is smaller, we yield it as Boundary only when
      // the required behaviour is to yield boundary at the beginning
      if win.Length < size then
        if boundary = Boundary.AtBeginning then yield DataSegment(Incomplete, win)
      else yield DataSegment(Complete, win)
      windows.[currentWindow.Value] <- []
      currentWindow := (!currentWindow + 1) % size
    // If we are supposed to generate boundary at the end, do it now
    if boundary = Boundary.AtEnding then
      for _ in 1 .. size - 1 do
        yield DataSegment(Incomplete, windows.[currentWindow.Value] |> Array.ofList |> Array.rev)
        currentWindow := (!currentWindow + 1) % size }


  /// Similar to `Seq.windowedWithBounds`, but generates non-overlapping chunks
  /// rather than floating windows. See that function for detailed documentation.
  /// The function may iterate over the sequence repeatedly.
  let chunkedWithBounds size (boundary:Boundary) input = seq {
    // If the user wants incomplete chunk at the beginning, we 
    // need to know the length of the whole sequence..
    let tail = ref input
    if boundary.HasFlag(Boundary.AtBeginning) then 
      let size = (Seq.length input) % size
      if size <> 0 && not (boundary.HasFlag(Boundary.Skip)) then
        yield DataSegment(Incomplete, Seq.take size input |> Array.ofSeq)
      tail := input |> Seq.skip size
    
    // Process the main part of the sequence
    let currentChunk = ref []
    let currentChunkSize = ref 0
    for v in !tail do
      currentChunk := v::currentChunk.Value
      incr currentChunkSize
      if !currentChunkSize = size then
        yield DataSegment(Complete, !currentChunk |> Array.ofList |> Array.rev)
        currentChunk := []
        currentChunkSize := 0 
        
    // If we want to yield incomplete chunks at the end and we got some, yield now
    if boundary.HasFlag(Boundary.AtEnding) && !currentChunk <> [] &&
       not (boundary.HasFlag(Boundary.Skip)) then
       yield DataSegment(Incomplete, !currentChunk |> Array.ofList |> Array.rev) }


  /// Returns true if the specified sequence is sorted.
  let isSorted (data:seq<_>) (comparer:IComparer<_>) =
    let rec isSorted past (en:IEnumerator<'T>) =
      if not (en.MoveNext()) then true
      elif comparer.Compare(past, en.Current) > 0 then false
      else isSorted en.Current en
    let en = data.GetEnumerator()
    if not (en.MoveNext()) then true
    else isSorted en.Current en

  /// Returns the first and the last element from a sequence or 'None' if the sequence is empty
  let tryFirstAndLast (input:seq<_>) = 
    let mutable first = None
    let mutable last = None
    for v in input do 
      if first.IsNone then first <- Some v
      last <- Some v
    let last = last
    first |> Option.map (fun f -> f, last.Value)

  /// Align two ordered sequences of `Key * Address` pairs and produce a 
  /// collection that contains three-element tuples consisting of: 
  ///
  ///   * ordered keys (from one or the ohter sequence)
  ///   * optional address of the key in the first sequence
  ///   * optional address of the key in the second sequence
  ///
  let alignWithOrdering (seq1:seq<'T * 'TAddress>) (seq2:seq<'T * 'TAddress>) (comparer:IComparer<_>) = seq {
    let withIndex seq = Seq.mapi (fun i v -> i, v) seq
    use en1 = seq1.GetEnumerator()
    use en2 = seq2.GetEnumerator()
    let en1HasNext = ref (en1.MoveNext())
    let en2HasNext = ref (en2.MoveNext())
    let returnAll (en:IEnumerator<_>) hasNext f = seq { 
      if hasNext then
        yield f en.Current
        while en.MoveNext() do yield f en.Current }

    let rec next () = seq {
      if not en1HasNext.Value then yield! returnAll en2 en2HasNext.Value (fun (k, i) -> k, None, Some i)
      elif not en2HasNext.Value then yield! returnAll en1 en1HasNext.Value (fun (k, i) -> k, Some i, None)
      else
        let en1Val, en2Val = fst en1.Current, fst en2.Current
        let comparison = comparer.Compare(en1Val, en2Val)
        if comparison = 0 then 
          yield en1Val, Some(snd en1.Current), Some(snd en2.Current)
          en1HasNext := en1.MoveNext()
          en2HasNext := en2.MoveNext()
          yield! next()
        elif comparison < 0 then
          yield en1Val, Some(snd en1.Current), None
          en1HasNext := en1.MoveNext()
          yield! next ()
        else 
          yield en2Val, None, Some(snd en2.Current)
          en2HasNext := en2.MoveNext() 
          yield! next () }
    yield! next () }

  /// Align two unordered sequences of `Key * Address` pairs and produce a collection
  /// that contains three-element tuples consisting of keys, optional address in the
  /// first sequence & optional address in the second sequence. (See also `alignWithOrdering`)
  let alignWithoutOrdering (seq1:seq<'T * 'TAddress>) (seq2:seq<'T * 'TAddress>) = seq {
    let dict = Dictionary<_, _>()
    for key, addr in seq1 do
      dict.[key] <- (Some addr, None)
    for key, addr in seq2 do
      match dict.TryGetValue(key) with
      | true, (left, _) -> dict.[key] <- (left, Some addr)
      | _ -> dict.[key] <- (None, Some addr)
    for (KeyValue(k, (l, r))) in dict do
      yield k, l, r }


/// An interface implemented by types that support nice formatting for F# Interactive
/// (The `FSharp.DataFrame.fsx` file registers an FSI printer using this interface.)
type IFsiFormattable =
  abstract Format : unit -> string

module Formatting = 
  /// Maximal number of items to be printed at the beginning of a series/frame
  let StartItemCount = 15
  /// Maximal number of items to be printed at the end of a series/frame
  let EndItemCount = 15

  open System
  open System.IO
  open System.Text

  // Simple functions that pretty-print series and frames
  // (to be integrated as ToString and with F# Interactive)
  let formatTable (data:string[,]) =
    let sb = StringBuilder()
    use wr = new StringWriter(sb)

    let rows = data.GetLength(0)
    let columns = data.GetLength(1)
    let widths = Array.zeroCreate columns 
    data |> Array2D.iteri (fun r c str ->
      widths.[c] <- max (widths.[c]) (str.Length))
    for r in 0 .. rows - 1 do
      for c in 0 .. columns - 1 do
        wr.Write(data.[r, c].PadRight(widths.[c] + 1))
      wr.WriteLine()

    sb.ToString()