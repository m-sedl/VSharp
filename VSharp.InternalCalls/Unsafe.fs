namespace VSharp.System

open global.System
open VSharp
open VSharp.Core

// ------------------------------ System.Unsafe --------------------------------

module Unsafe =

    let internal AsPointer (state : state) (args : term list) : term =
        assert(List.length args = 2)
        let ref = List.item 1 args
        let ptr = Types.CastReferenceToPointer state ref
        Types.Cast ptr (Pointer Void)

    let internal As (_ : state) (args : term list) : term =
        assert(List.length args = 2)
        let typ, ref = args.[0], args.[1]
        let typ =
            match typ.term with
            | Concrete(:? Type as t, _) -> Types.FromDotNetType t
            | _ -> __unreachable__()
        Types.Cast ref typ
