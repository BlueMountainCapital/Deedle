﻿(**
Feature overview
================

TODO

*)
#I "../bin"
#load "FSharp.DataFrame.fsx"
#load "../packages/FSharp.Charting.0.84/FSharp.Charting.fsx"
#r "../packages/FSharp.Data.1.1.9/lib/net40/FSharp.Data.dll"
open System
open FSharp.Data
open FSharp.DataFrame
open FSharp.Charting

(*
let s1 = Series.ofValues [ 1 .. 10 ]
let f1 = Frame.ofColumns [ "A" => s1 ]
f1.Append(f1) // TODO: Allow UnionBehavior?
f1.Join(f1)

s1.Union(s1)
s1.Join(s1) // TODO: Support lookup

f1
//*)

let dt = DateTime(2012, 2, 12)
let ts = TimeSpan.FromMinutes(5.37)
let inp = Seq.init 50 (fun i -> dt.Add(TimeSpan(ts.Ticks * int64 i)), i) |> Series.ofObservations

inp.Aggregate(WindowSize(10, Boundary.AtEnding), (fun r -> r), (fun ds -> ds.Data.KeyRange |> fst))

inp |> Series.sampleTimeInto (TimeSpan(1,0,0)) Direction.Forward Series.firstValue
inp |> Series.sampleTimeInto (TimeSpan(1,0,0)) Direction.Backward Series.lastValue

let ts2 = TimeSpan.FromHours(5.37)
let inp2 = Seq.init 20 (fun i -> dt.Add(TimeSpan(ts2.Ticks * int64 i)), i) |> Series.ofObservations
inp2 |> Series.sample [ DateTime(2012, 2, 13); DateTime(2012, 2, 15) ] Direction.Forward
inp2 |> Series.sample [ DateTime(2012, 2, 13); DateTime(2012, 2, 15) ] Direction.Backward

inp2 |> Series.sampleTimeInto (TimeSpan(24,0,0)) Direction.Forward Series.firstValue

let ts3 = TimeSpan.FromHours(48.0)
let inp3 = Seq.init 5 (fun i -> dt.Add(TimeSpan(ts3.Ticks * int64 i)), i) |> Series.ofObservations

let keys = [ for d in 12 .. 20 -> DateTime(2012, 2, d) ]
inp3 |> Series.sample keys Direction.Forward

(** 
Hiearrchical indexing
---------------------

*)

let wb = WorldBankData.GetDataContext()

let loadRegion (region:WorldBankData.ServiceTypes.Region) =
  [ for country in region.Countries -> 
      (region.Name, country.Name) => 
        Series.ofObservations country.Indicators.``GDP (current US$)`` ]
  |> Frame.ofColumns

let df1 = loadRegion wb.Regions.``Euro area``
let df2 = loadRegion wb.Regions.``OECD members``
let world = df1.Join(df2)

// F# 3.0 needs this
world.Columns.[Lookup1Of2 "Euro area"]
world.Columns.[Lookup1Of2 "Euro area"]
world.Columns.[Lookup1Of2 "Euro area"].Columns.[Lookup2Of2 "Austria"]
world.Columns.[Lookup2Of2 "Mexico"]
world.Columns.[Lookup2Of2 "Belgium"]

// F# 3.1 makes this way nicer
world.Columns.["Euro area", *]
world.Columns.[*, "Belgium"]
world.Columns.[("Euro area", "Belgium")]

let euro = 
  world.Columns.[Lookup1Of2 "Euro area"]
  |> Frame.mapColKeys snd

let grouped = 
  euro
  |> Frame.groupColsUsing (fun k _ -> k.Substring(0, 1))
  |> Frame.orderCols
  |> Frame.groupRowsUsing (fun k _ -> sprintf "%d0s" (k / 10))

grouped.Rows.["1990s", *].Columns.["F", *]

grouped.Columns.[("A", "Austria")].As<float>() / 1.0e9
|> Series.meanBy fst

grouped.Columns.[("C", "Cyprus")].As<float>() / 1.0e9
|> Series.meanBy fst

grouped / 1.0e9
|> Frame.meanBy fst

grouped / 1.0e9
|> Frame.transpose
|> Frame.meanBy fst

grouped / 1.0e9
|> Frame.meanBy fst
|> Frame.transpose
|> Frame.meanBy fst

let oddEvenGroups =
  euro / 1.0e9
  |> Frame.mapRowKeys (fun rk -> (sprintf "%ds" (rk/10*10), ((if rk%2 = 0 then "even" else "odd"), rk)))
  |> Frame.orderRows

oddEvenGroups 
|> Frame.meanBy fst


let titanic = Frame.readCsv(__SOURCE_DIRECTORY__ + "/data/Titanic.csv")
let byClassAndPort1 = titanic.GroupRowsBy<int>("Pclass").GroupRowsBy<string>("Embarked") |> Frame.mapRowKeys Tuple.flatten3
let byClassAndPort = 
  titanic
  |> Frame.groupRowsByInt "Pclass"
  |> Frame.groupRowsByString "Embarked"
  |> Frame.mapRowKeys Tuple.flatten3


let ageByClassAndPort = byClassAndPort.Columns.["Age"].As<float>()

Frame.ofColumns
  [ "AgeMeans", ageByClassAndPort |> Series.meanBy Tuple.get1And2Of3
    "AgeCounts", float $ (ageByClassAndPort |> Series.countBy Tuple.get1And2Of3) ]

byClassAndPort
|> Frame.meanBy Tuple.get1And2Of3

byClassAndPort
|> Frame.sumBy Tuple.get1And2Of3

let survivedByClassAndPort = byClassAndPort.Columns.["Survived"].As<bool>()

// survivedByClassAndPort
// |> Series.meanBy By1Of3

survivedByClassAndPort 
|> Series.foldBy Tuple.get1And2Of3 (fun sr -> sprintf "%A" (sr.Values |> Seq.countBy id |> List.ofSeq))

byClassAndPort
|> Frame.foldBy Tuple.get1And2Of3 (fun sr -> sr.CountValues())



let sample = Frame.ofColumns [ "Test" => Series.ofValues [ 1.0 .. 4.0 ] ]
let groupedSample = sample.ReplaceRowIndexKeys [ ("Small", 0); ("Small", 1); ("Big", 0); ("Big", 1) ] |> Frame.orderRows

(** 
Overlaoded slicing
------------------
*)

let wb2 = WorldBankData.GetDataContext()

let arab = 
  Frame.ofColumns
    [ for country in wb2.Regions.``Arab World``.Countries -> 
        country.Name => Series.ofObservations country.Indicators.``GDP (current US$)`` ]

arab.Columns.[ ["Algeria"; "Bahrain"] ]
|> Frame.mean

arab.Rows.[ 2000 .. 2012 ]



(**
Joining series
--------------
*)

// sortBy : seq<'T> -> orderdSeq<'T>
// thenBy : orderdSeq<'T> -> orderdSeq<'T>


(*
query { for n in [ 1 .. 10 ] do
        let m = n + 1 
        sortBy n
        thenBy m
        select (string n) into g
        take 10
        where (n > 4)
        select n }
// 
frame { for r in frame do
        withIndex (r.GetAs<string>("Name"))
        shift 1
        window 5 into win 
        ... } 
*)

let sa = Series.ofObservations [ 1 => "a"; 2 => "b" ]
let sb = Series.ofObservations [ 3 => "c"; 2 => "b" ]

sa.Join(sb, JoinKind.Inner, Lookup.Exact)
sa.Join(sb, JoinKind.Left, Lookup.Exact)
sa.Join(sb, JoinKind.Outer, Lookup.Exact)

(**
Fancy windowing & chunking
--------------------------
*)

let st = Series.ofValues [ 'a' .. 'j' ]
st |> Series.windowSize (3, Boundary.Skip) |> Series.map (fun _ v -> String(Array.ofSeq v.Values))
st |> Series.windowSize (3, Boundary.AtBeginning) |> Series.map (fun _ v -> String(Array.ofSeq v.Values))
st |> Series.windowSize (3, Boundary.AtEnding) |> Series.map (fun _ v -> String(Array.ofSeq v.Values))

let concatString = function
  | DataSegment.Complete(ser) -> String(ser |> Series.values |> Array.ofSeq)
  | DataSegment.Incomplete(ser) -> String(ser |> Series.values |> Array.ofSeq).PadRight(3, '-')

st |> Series.chunkSizeInto (3, Boundary.Skip) concatString
st |> Series.chunkSizeInto (3, Boundary.AtBeginning) concatString
st |> Series.chunkSizeInto (3, Boundary.AtEnding) concatString

(** 
Grouping 
--------
*)

// Load the data from the Titanic data set
let titanic = Frame.readCsv(__SOURCE_DIRECTORY__ + "/data/Titanic.csv")

// Group the data frame by sex 
let grouped = titanic |> Frame.groupRowsBy "Sex"

// For each group, calculate the total number of survived & died
let survivalBySex =
  grouped 
  |> Series.map (fun sex df -> 
      // Group each sex by the Survived column & count elements in each group
      df.GetSeries<bool>("Survived") |> Series.groupBy (fun k v -> v) 
      |> Frame.ofColumns |> Frame.countValues )
  |> Frame.ofRows
  |> Frame.withColumnKeys ["Died"; "Survived"]

// Add column with Total number of males/females on Titanic
survivalBySex?Total <- grouped |> Series.map (fun _ -> Frame.countKeys)

// Build a data frame with nice summary of rates in percents
let results =
  [ "Died (%)" => survivalBySex?Died / survivalBySex?Total * 100.0
    "Survived (%)" => survivalBySex?Survived / survivalBySex?Total * 100.0 ]
  |> Frame.ofColumns


(**
Filling missing values
----------------------
*)

let nulls =
  [ Nullable(1); Nullable(); Nullable(3); Nullable(4) ]
  |> Series.ofValues
  |> Series.map (fun _ v -> v.Value)

let test = Frame.ofColumns [ "Test" => nulls ]
test?Test

[ Nullable(1); Nullable(); Nullable(3); Nullable(4) ]
|> Series.ofNullables


let air = Frame.ReadCsv(__SOURCE_DIRECTORY__ + "/data/AirQuality.csv", separators=";")
let ozone = air?Ozone

air?OzoneFilled <-
  ozone |> Series.mapAll (fun k -> function 
    | None -> ozone.TryGet(k, Lookup.NearestSmaller)
    | value -> value)

// Might not be super usefu but does the trick
ozone |> Series.fillMissingWith 0.0
// This works
ozone |> Series.fillMissing Direction.Backward
// but here, the first value is still missing
ozone |> Series.fillMissing Direction.Forward
// we can be more explicit and write
ozone |> Series.fillMissingUsing (fun k -> 
  defaultArg (ozone.TryGet(k, Lookup.NearestSmaller)) 0.0)
// Or we can drop the first value
ozone |> Series.fillMissing Direction.Forward
      |> Series.dropMissing



// air.WithRowIndex<int>("Month")

// System.Linq.Enumerable.group
// air.Rows.Aggregate(Aggregation.GroupBy(



(*

let f = Frame.ofColumns ["C" => Series.ofValues [ 1;2;3 ]]
f.Append(f)

// TODO:
let f = Frame.ofColumns ["C" => Series.ofObservations [ 1,"hi"; 2,"there"; 3,"ciao" ]]
f.Append(f)

*)

(**
Operations
----------
*)

let rnd = Random()
let s = Series.ofValues [ for i in 0 .. 100 -> rnd.NextDouble() * 10.0 ]

log s 
log10 s

let s1 = abs (log s) * 10.0
floor s1 - round s1  

Series.Log10


(**
Grouping
--------

*)
type Person = 
  { Name:string; Age:int; Countries:string list; }

let people = 
  [ { Name = "Joe"; Age = 51; Countries = [ "UK"; "US"; "UK"] }
    { Name = "Tomas"; Age = 28; Countries = [ "CZ"; "UK"; "US"; "CZ" ] }
    { Name = "Eve"; Age = 2; Countries = [ "FR" ] }
    { Name = "Suzanne"; Age = 15; Countries = [ "US" ] } ]

// Turn the list of records into data frame and use 
// the 'Name' column (containing strings) as the key
let dfList = Frame.ofRecords people 
let df = dfList |> Frame.withRowIndex (column<string>("Name"))

let peopleCountries = 
  df.GetSeries<string list>("Countries")
  |> Series.map (fun k countries -> 
      [ for c, k in Seq.countBy id countries -> c, k ] |> Series.ofObservations )
  |> Frame.ofRows
  |> Frame.withMissingVal 0

let sums = peopleCountries |> Frame.sum 

sums.Observations
|> Seq.sortBy (fun (c, v) -> -v)
|> Chart.Column


(* More options when working with records *)
let recdSeries = 
  Series.ofValues [ "Tomas"; null; "Joe"; "Eve" ]
  |> Series.map (fun _ v -> { Name = v; Age = -1; Countries = [] })

// Missing values are preserved at the right index..
Frame.ofRecords recdSeries



let dfPeople = Frame.ofRecords people 
dfPeople?CountryCount <- List.length $ dfPeople.GetSeries<string list>("Countries")
dfPeople |> Frame.groupRowsBy






(* TEST *)

let f1 = Frame.ofRows [ 1 => Series.ofObservations [ "A" => 1; "B" => 2 ] ]
let f2 = Frame.ofRows [ 2 => Series.ofObservations [ "C" => 3 ] 
                        3 => Series.ofObservations [ "C" => 4 ]  ]
f1.Append(f2)

