namespace OpenAPITypeProvider

open ProviderImplementation.ProvidedTypes
open Microsoft.FSharp.Core.CompilerServices
open System.Reflection
open OpenAPITypeProvider.Types
open OpenAPIProvider.Common

[<TypeProvider>]
type OpenAPITypeProviderImplementation (cfg : TypeProviderConfig) as this =
   inherit TypeProviderForNamespaces (cfg)

   do System.AppDomain.CurrentDomain.add_AssemblyResolve(fun _ args ->
        let expectedName = (AssemblyName(args.Name)).Name + ".dll"
        let asmPath = 
            cfg.ReferencedAssemblies
            |> Seq.tryFind (fun asmPath -> System.IO.Path.GetFileName(asmPath) = expectedName)
        match asmPath with
        | Some f -> Assembly.LoadFrom f
        | None -> null)
    
   let ns = "OpenAPIProvider"
   let asm = Assembly.GetExecutingAssembly()
    
   let tp = ProvidedTypeDefinition(asm, ns, "OpenAPIV3Provider", None,  hideObjectMethods = true, nonNullable = true, isErased = true)
    
   let createTypes typeName (args:obj[]) =
       try
           let filePath = args.[0] :?> string
           let dateFormatString = args.[1] :?> string
           
           let toNewtonsoft (value:DateTimeZoneHandling) =
                match value with
                | DateTimeZoneHandling.Local -> Newtonsoft.Json.DateTimeZoneHandling.Local
                | DateTimeZoneHandling.Utc -> Newtonsoft.Json.DateTimeZoneHandling.Utc
                | DateTimeZoneHandling.Unspecified -> Newtonsoft.Json.DateTimeZoneHandling.Unspecified
                | DateTimeZoneHandling.RoundtripKind | _ -> Newtonsoft.Json.DateTimeZoneHandling.RoundtripKind

           Json.Serialization.settings.DateFormatString <- dateFormatString
           
           let ctx = { Assembly = asm; Namespace = ns }

           filePath 
           |> OpenAPITypeProvider.IO.getFilename cfg
           |> Document.createType ctx typeName
        with ex -> 
            let rec getMsg list (ex:System.Exception) = 
                match ex.InnerException with
                | null -> (sprintf "Error: %s, StackTrace: %s" ex.Message ex.StackTrace) :: list
                | x -> x |> getMsg list
            ex |> getMsg [] |> List.rev |> String.concat ", " |> failwith
    
   let parameters = [ 
         ProvidedStaticParameter("FilePath", typeof<string>)
         ProvidedStaticParameter("DateFormatString", typeof<string>, parameterDefaultValue = "yyyy-MM-ddTHH:mm:ss.FFFFFFFK") 
       ]
   
   let helpText = 
        """<summary>OpenAPI Version 3 Type Provider</summary>
           <param name='FilePath'>Location of a YAML file with specification.</param>
           <param name='DateFormatString'>Specifies format for (de)serialization of DateTime. Default is 'yyyy-MM-ddTHH:mm:ss.FFFFFFFK'.</param>
           """
  
   do tp.AddXmlDoc helpText
   do tp.DefineStaticParameters(parameters, createTypes)
   do this.AddNamespace(ns, [tp])

[<assembly:TypeProviderAssembly()>]
do ()