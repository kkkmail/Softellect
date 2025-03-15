namespace Softellect.Math

open Softellect.Math.Primitives
open Softellect.Math.Sparse
open System

/// TODO kk:20250315 - This is not generic enough to be sitting here.
///     However, I need to run some tests to see how all the new logic works.
module Models =

    type NoOfEpochs =
        | NoOfEpochs of int

        member r.value = let (NoOfEpochs v) = r in v


    type FoodData =
        | FoodData of int64

        member r.value = let (FoodData v) = r in v


    type WasteData =
        | WasteData of int64

        member r.value = let (WasteData v) = r in v


    type ProtoСellData<'I when ^I: equality and ^I: comparison> =
        | ProtoСellData of SparseArray<'I, int64>

        member r.value = let (ProtoСellData v) = r in v
        member r.total() = r.value.total()


    type SubstanceData<'I when ^I: equality and ^I: comparison> =
        {
            food : FoodData
            waste : WasteData
            protocell : ProtoСellData<'I>
        }


    type MoleculeCount =
        | MoleculeCount of int64

        member r.value = let (MoleculeCount v) = r in v
        static member OneThousand = MoleculeCount 1_000L // 10^3 - K
        static member OneMillion = MoleculeCount 1_000_000L // 10^6 - M
        static member OneBillion = MoleculeCount 1_000_000_000L // 10^9 - G
        static member OneTrillion = MoleculeCount 1_000_000_000_000L // 10^12 - T
        static member OneQuadrillion = MoleculeCount 1_000_000_000_000_000L // 10^15 - P
        static member OneQuintillion = MoleculeCount 1_000_000_000_000_000_000L // 10^18 - E


    type ModelInitParams =
        {
            uInitial : MoleculeCount
            // protocellInitParams : EeInfDiffModel.ProtocellInitParams
            totalMolecules : MoleculeCount
            seedValue : int
        }

        static member defaultValue =
            {
                uInitial = MoleculeCount.OneThousand
                // protocellInitParams = EeInfDiffModel.ProtocellInitParams.defaultValue
                totalMolecules = MoleculeCount.OneBillion
                seedValue = 1
            }


    // type Kernel<'I when ^I: equality and ^I: comparison> =
    //     {
    //         ka : 'I -> double
    //         kernel : SparseMatrix<'I, double>
    //     }
