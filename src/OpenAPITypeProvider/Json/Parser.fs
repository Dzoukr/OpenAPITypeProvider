module OpenAPITypeProvider.Json.Parser

open System
open OpenAPIParser.Version3.Specification
open Newtonsoft.Json.Linq
open OpenAPITypeProvider

let private checkRequiredProperties (req:string list) (jObject:JObject) =
    let propertyExist name = jObject.Properties() |> Seq.exists (fun x -> x.Name = name)
    req |> List.iter (fun p ->
        if propertyExist p |> not then failwith (sprintf "Property '%s' is required by schema definition, but not present in JSON" p)
    )

type ReflectiveListBuilder = 
    static member BuildList<'a> (args: obj list) = 
        [ for a in args do yield a :?> 'a ] 
    static member BuildTypedList lType (args: obj list) = 
        typeof<ReflectiveListBuilder>
            .GetMethod("BuildList")
            .MakeGenericMethod([|lType|])
            .Invoke(null, [|args|])


// let private some (typ:Type) arg =
//     let unionType = typedefof<option<_>>.MakeGenericType typ
//     let meth = unionType.GetMethod("Some")
//     meth.Invoke(null, [|arg|])


open Microsoft.FSharp.Reflection

let some typ arg = 
  let cases = FSharpType.GetUnionCases(typedefof<option<_>>.MakeGenericType([| typ |]))
  let case = cases |> Seq.find (fun c -> c.Name = "Some")
  FSharpValue.MakeUnion(case, [| arg |])

// type MakeSomeHelper<'T> = 
//   static member MakeSome<'T>(v:'T) = Some(v)

// // let some2 (typ:System.Type) arg =
// //   typeof<MakeSomeHelper>
// //     .GetMethod("MakeSome")
// //     .MakeGenericMethod(typ)
// //     .Invoke(null, [|arg|])



// let some (typ:System.Type) arg =
//   typedefof<MakeSomeHelper<obj>>.MakeGenericType([| typ |]).GetMethod("MakeSome").Invoke(null, [|arg|])

//let trySome (typ:System.Type) arg =


//let private some (typ:Type) arg =
    


    //Option.Some arg |> box

    // System.Console.WriteLine "*************************************"
    // typ |> System.Console.WriteLine
    // System.Console.WriteLine "*************************************"
    
    // let unionType = typedefof<option<_>>.MakeGenericType typ
    // let meth = unionType.GetMethod("Some")
    // meth.Invoke(null, [|arg|])

    // let unionType = 
    //     if typ.IsGenericType then
    //         System.Console.WriteLine "HEEEEEEEEEEER"
    //         let t = typedefof<List<_>>
    //         typedefof<Option<_>>.MakeGenericType t
    //     else
    //         typedefof<Option<_>>.MakeGenericType typ
    // let meth = unionType.GetMethod("Some")
    // meth |> System.Console.WriteLine
    // meth.Invoke(null, [|arg|])

// let typ = ["AB"].GetType()
// let unionType = typedefof<option<int>>
// let unionType2 = typedefof<option<_>>.MakeGenericType typedefof<_>
// let meth = unionType2.GetMethod("Some")
// let meth2 = meth.MakeGenericMethod(unionType2)
// meth.Invoke(null, [||])

// let unionType = typedefof<option<_>> // typ
// unionType.GetMethod("Some")
// typ :> Type
// let unionType = typedefof<option<_>>.MakeGenericType typ


let rec parseForSchema createObj (schema:Schema) (json:JToken) =
    match schema with
    | Boolean -> json.Value<bool>() |> box
    | Integer Int32 -> json.Value<int32>() |> box
    | Integer Int64 -> json.Value<int64>() |> box
    | Number NumberFormat.Double -> json.Value<double>() |> box
    | Number NumberFormat.Float -> json.Value<float32>() |> box
    | String StringFormat.String 
    | String StringFormat.Binary 
    | String StringFormat.Password -> json.Value<string>() |> box
    | String StringFormat.Byte -> json.Value<byte>() |> box
    | String StringFormat.DateTime
    | String StringFormat.Date -> json.Value<DateTime>() |> box
    | Array itemsSchema ->
        let jArray = json :?> JArray
        let items = [ for x in jArray do yield parseForSchema createObj itemsSchema x ]
        let typ = itemsSchema |> Inference.getComplexType (fun _ -> typeof<obj>)
        ReflectiveListBuilder.BuildTypedList typ items |> box
    | Object (props, required) ->
        let jObject = json :?> JObject
        jObject |> checkRequiredProperties required
        props 
        |> Map.map (fun name schema -> 
            if required |> List.contains name then
                parseForSchema createObj schema (jObject.[name]) |> Some
            else if jObject.ContainsKey name then
                let typ = schema |> Inference.getComplexType (fun _ -> typeof<obj>)
                parseForSchema createObj schema (jObject.[name]) 
                |> some typ
                |> Some
            else None
        )
        |> Map.filter (fun _ v -> v.IsSome)
        |> Map.map (fun _ v -> v.Value)
        |> Map.toList
        |> createObj
        |> box