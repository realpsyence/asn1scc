﻿module genericBackend

open System.Numerics
open FsUtils
open Ast
open System.IO
open VisitTree
open uPER
open CloneTree
open spark_utils




let rec PrintType (t:Asn1Type) modName (r:AstRoot) (stgFileName:string)=
    let GetMinMax uperRange = 
        match uperRange with
        | Concrete(min, max)      -> min.ToString(), max.ToString()
        | PosInf(a)               -> a.ToString(), "MAX"
        | NegInf(max)             -> "MIN", max.ToString()
        | Full                    -> "MIN", "MAX"
        | Empty                   -> raise(BugErrorException "")
    let handTypeWithMinMax name uperRange func =
        let sMin, sMax = GetMinMax uperRange
        func name sMin sMax stgFileName
    let PrintTypeAux (t:Asn1Type) =
        match t.Kind with
        | Integer               -> handTypeWithMinMax "IntegerType"         (GetTypeUperRange t.Kind t.Constraints r) gen.MinMaxType //(fun n min max -> gen.MinMaxType n min max stgFileName)
        | BitString             -> handTypeWithMinMax "BitStringType"       (GetTypeUperRange t.Kind t.Constraints r) gen.MinMaxType2
        | OctetString           -> handTypeWithMinMax "OctetStringType"     (GetTypeUperRange t.Kind t.Constraints r) gen.MinMaxType2
        | Real                  -> handTypeWithMinMax "RealType"            (GetTypeRange_real t.Kind t.Constraints r) gen.MinMaxType
        | IA5String             -> handTypeWithMinMax "IA5StringType"       (GetTypeUperRange t.Kind t.Constraints r) gen.MinMaxType2
        | NumericString         -> handTypeWithMinMax "NumericStringType"   (GetTypeUperRange t.Kind t.Constraints r) gen.MinMaxType2
        | Boolean               -> gen.BooleanType () stgFileName
        | NullType              -> gen.NullType () stgFileName
        | Choice(children)      -> 
            let emitChild (c:ChildInfo) =
                gen.ChoiceChild c.Name.Value (BigInteger c.Name.Location.srcLine) (BigInteger c.Name.Location.charPos) (PrintType c.Type modName r stgFileName) (c.CName_Present C) stgFileName
            gen.ChoiceType (children |> Seq.map emitChild) stgFileName
        | Sequence(children)    -> 
            let emitChild (c:ChildInfo) =
                match c.Optionality with
                | Some(Default(vl)) -> gen.SequenceChild c.Name.Value true (PrintAsn1.PrintAsn1Value vl) (BigInteger c.Name.Location.srcLine) (BigInteger c.Name.Location.charPos) (PrintType c.Type modName r stgFileName) stgFileName
                | _                 -> gen.SequenceChild c.Name.Value c.Optionality.IsSome null (BigInteger c.Name.Location.srcLine) (BigInteger c.Name.Location.charPos) (PrintType c.Type modName r stgFileName) stgFileName
            gen.SequenceType (children |> Seq.map emitChild) stgFileName
        | Enumerated(items)     -> 
            let emitItem (idx: int) (it : Ast.NamedItem) =
                match it._value with
                | Some(vl)  -> gen.EnumItem it.Name.Value (Ast.GetValueAsInt vl r) (BigInteger it.Name.Location.srcLine) (BigInteger it.Name.Location.charPos) (it.CEnumName r C) stgFileName
                | None      -> gen.EnumItem it.Name.Value (BigInteger idx) (BigInteger it.Name.Location.srcLine) (BigInteger it.Name.Location.charPos) (it.CEnumName r C) stgFileName
            gen.EnumType (items |> Seq.mapi emitItem) stgFileName
        | SequenceOf(child)     -> 
            let sMin, sMax = GetMinMax (GetTypeUperRange t.Kind t.Constraints r) 
            gen.SequenceOfType sMin sMax  (PrintType child modName r stgFileName) stgFileName
        | ReferenceType(md,ts, _) ->  
            let uperRange = 
                match (Ast.GetActualType t r).Kind with
                | Integer | BitString | OctetString | IA5String | NumericString | SequenceOf(_)         -> Some (GetMinMax (GetTypeUperRange t.Kind t.Constraints r) )
                | Real                                                                                  -> Some (GetMinMax (GetTypeRange_real t.Kind t.Constraints r))
                | Boolean | NullType | Choice(_) | Enumerated(_) | Sequence(_) | ReferenceType(_)       -> None
            let sModName = if md.Value=modName then null else md.Value
            match uperRange with
            | Some(sMin, sMax)  -> gen.RefTypeMinMax sMin sMax ts.Value sModName stgFileName
            | None              -> gen.RefType ts.Value sModName stgFileName
            
    gen.TypeGeneric (BigInteger t.Location.srcLine) (BigInteger t.Location.charPos) (PrintTypeAux t) stgFileName


let DoWork (r:AstRoot) (stgFileName:string) (outFileName:string) =
    let PrintTas (tas:Ast.TypeAssignment) modName =
        gen.TasXml tas.Name.Value (BigInteger tas.Name.Location.srcLine) (BigInteger tas.Name.Location.charPos) (PrintType tas.Type modName r stgFileName) stgFileName
    let PrintModule (m:Asn1Module) =
        let PrintImpModule (im:Ast.ImportedModule) =
            gen.ImportedMod im.Name.Value (im.Types |> Seq.map(fun x -> x.Value)) (im.Values |> Seq.map(fun x -> x.Value)) stgFileName
        gen.ModuleXml m.Name.Value (m.Imports |> Seq.map PrintImpModule) m.ExportedTypes m.ExportedVars (m.TypeAssignments |> Seq.map (fun t -> PrintTas t m.Name.Value)) stgFileName
    let PrintFile (f:Asn1File) =
        gen.FileXml f.FileName (f.Modules |> Seq.map PrintModule) stgFileName
    let content = gen.RootXml (r.Files |> Seq.map PrintFile) stgFileName
    File.WriteAllText(outFileName, content.Replace("\r",""))
    




