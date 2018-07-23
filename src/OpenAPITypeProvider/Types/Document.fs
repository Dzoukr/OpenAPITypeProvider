module internal OpenAPITypeProvider.Types.Document

open ProviderImplementation.ProvidedTypes
open OpenAPIParser.Version3
open OpenAPIParser.Version3.Specification
open OpenAPITypeProvider.Types.Helpers

open System

let mutable allSchemas : Map<Schema,SchemaType> = Map.empty

let rec private uniqueName (name:string) =
    match allSchemas |> Map.exists (fun _ v -> v.Name = name) with
    | true -> 
        let nameParts = name.Split([|'_'|], StringSplitOptions.RemoveEmptyEntries)
        let newName = 
            match nameParts |> Array.tryLast with
            | None -> name + "_1"
            | Some v -> 
                match v |> Int32.TryParse with
                | true, i -> 
                    nameParts 
                    |> Array.filter (fun x -> x <> v)
                    |> Array.fold (+) ""
                    |> fun x -> sprintf "%s_%i" x (i + 1) 
                | false, _ -> name + "_1"
        newName |> uniqueName
    | false -> name

let createType ctx typeName (filePath:string) =
    
    allSchemas <- Map.empty

    let typ = ProvidedTypeDefinition(ctx.Assembly, ctx.Namespace, typeName, None, hideObjectMethods = true, nonNullable = true, isErased = true)
    let api = filePath |> Parser.Document.loadFromYamlFile

    // ctor
    ProvidedConstructor([], fun _ -> <@@ obj() @@>) |> typ.AddMember

    // version    
    let version = api.SpecificationVersion
    ProvidedProperty("Version", typeof<string>, (fun _ -> <@@ version @@>)) |> typ.AddMember

    // info object
    let info = Info.createType ctx api.Info
    info |> typ.AddMember
    ProvidedProperty("Info", info, fun _ -> <@@ obj() @@>) |> typ.AddMember
    
    // Schemas
    let schemas = ProvidedTypeDefinition(ctx.Assembly, ctx.Namespace, "Schemas", None, hideObjectMethods = true, nonNullable = true, isErased = true)
    
    // Responses
    let responses = ProvidedTypeDefinition(ctx.Assembly, ctx.Namespace, "Responses", None, hideObjectMethods = true, nonNullable = true, isErased = true)
    
    // Request bodies
    let requestBodies = ProvidedTypeDefinition(ctx.Assembly, ctx.Namespace, "RequestBodies", None, hideObjectMethods = true, nonNullable = true, isErased = true)

    let rec findOrCreateSchema name (schema:Schema) =
        match allSchemas |> Map.tryFind schema with
        | Some t -> t
        | None ->
            let name = uniqueName name
            let newType =
                match schema with
                | Object _ -> Schema.Object.createTypes ctx findOrCreateSchema name schema
                | _ -> Schema.NonObject.createTypes ctx findOrCreateSchema name schema
            newType |> schemas.AddMember
            let schemaType = { Name = name; Type = newType }
            allSchemas <- allSchemas.Add(schema, schemaType)
            schemaType

    // components object
    if api.Components.IsSome then
        
            // Add object root types
            api.Components.Value.Schemas 
            |> Map.map findOrCreateSchema
            |> ignore
    
            // add responses
            if api.Components.Value.Responses.Count > 0 then
                api.Components.Value.Responses
                |> Map.iter (fun n p -> 
                    let resp = Response.createType ctx findOrCreateSchema n p
                    resp |> responses.AddMember
                    ProvidedProperty(n, resp, (fun _ -> <@@ obj() @@>), isStatic = true) 
                    |?> Some p.Description
                    |> responses.AddMember
                )

                responses |> typ.AddMember
                ProvidedProperty("Responses", responses, isStatic = true) |> typ.AddMember
    
            // add request bodies
            if api.Components.Value.RequestBodies.Count > 0 then
                api.Components.Value.RequestBodies
                |> Map.iter (fun n p -> 
                    let resp = RequestBody.createType ctx findOrCreateSchema n p
                    resp |> requestBodies.AddMember
                    ProvidedProperty(n, resp, (fun _ -> <@@ obj() @@>), isStatic = true) 
                    |?> p.Description
                    |> requestBodies.AddMember
                )
                requestBodies |> typ.AddMember
                ProvidedProperty("RequestBodies", requestBodies, isStatic = true) |> typ.AddMember


    // paths
    let paths = api.Paths |> Path.createTypes ctx findOrCreateSchema
    paths |> typ.AddMember
    ProvidedProperty("Paths", paths, fun _ -> <@@ obj() @@>) |> typ.AddMember
            
    // add schemas as last
    schemas |> typ.AddMember
    ProvidedProperty("Schemas", schemas, isStatic = true) |> typ.AddMember
            
    typ