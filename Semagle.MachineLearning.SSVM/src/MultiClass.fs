﻿// Copyright 2016 Serge Slipchenko (Serge.Slipchenko@gmail.com)
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Semagle.MachineLearning.SSVM

open Semagle.MachineLearning.SVM
open Semagle.Numerics.Vectors

/// Structured SVM model for multi-class classification
type MultiClass<'X,'Y> = MultiClass of FeatureFunction<'X> * SSVM<'X,'Y> * 'Y[]

module MultiClass =
    /// Optimzation parameters for Multi-Class Structured SVMs
    type Parameters<'Y> = { 
        /// The rescaling type
        rescaling : Rescaling;
        /// The penalty for slack variables  
        C : float32; 
        /// The maximum optimization error
        epsilon : float32;
        /// The loss function 
        loss : LossFunction<'Y>;
        /// General SMO algorithm optimization options
        options : SMO.OptimizationOptions  }

    /// Default optimzation parameters for Multi-Class Structured SVMs
    let defaults : Parameters<'Y> = 
        { rescaling = Slack; C = 1.0f; epsilon = 0.001f; 
          loss = (fun y y' -> if y = y' then 0.0f else 1.0f);
          options = { strategy = SMO.SecondOrderInformation; maxIterations = 1000000; 
                      shrinking = true; cacheSize = 200<MB> } } 

    let private index Y k i = let K = Array.length Y in i*K + k

    /// Learn structured SVM model for multi-class classification
    let learn (X: 'X[]) (Y : 'Y[]) (F : FeatureFunction<'X>) (parameters : Parameters<'Y>) =
        let YS = Array.distinct Y

        let JF x y =  
            let f = F x 
            SparseVector(Array.map (index YS (Array.findIndex ((=) y) YS)) f.Indices, f.Values)

        let argmaxLoss model i = 
            match model with
            | OneSlack(W) ->
                let F_i = JF X.[i] Y.[i]
                YS |> Array.map (fun y ->
                    let dF = F_i - (JF X.[i] y)
                    let WF = W .* dF
                    let loss = parameters.loss Y.[i] y
                    let m = match parameters.rescaling with | Slack -> loss | Margin -> 1.0f
                    let cost = loss - m * WF
                    y, loss, dF, cost)
                |> Array.maxBy (fun (_, _, _, cost) -> cost)

        let ssvm = OneSlack.optimize X Y JF 
                                     { rescaling = parameters.rescaling; 
                                       C = parameters.C; epsilon = parameters.epsilon;
                                       loss = parameters.loss; argmaxLoss = argmaxLoss;
                                       options = parameters.options }

        MultiClass(F, ssvm, YS)

    /// Predict class by multi-class structured SVM model
    let predict (model : MultiClass<'X,'Y>) (x : 'X) : 'Y =
        match model with
        | MultiClass(F, OneSlack(W), Y) -> 
            let f = F x 
            Y |> Array.maxBy (fun y -> 
                let index = index Y (Array.findIndex ((=) y) Y)
                Array.fold2(fun sum i v -> sum + W.[index i]*v) 0.0f f.Indices f.Values)