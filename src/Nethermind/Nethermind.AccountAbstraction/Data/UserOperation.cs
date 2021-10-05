//  Copyright (c) 2021 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
// 

using System;
using System.Collections.Generic;
using Nethermind.Abi;
using Nethermind.AccountAbstraction.Broadcaster;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Eip2930;
using Nethermind.Core.Extensions;
using Nethermind.Int256;

namespace Nethermind.AccountAbstraction.Data
{
    public partial class UserOperation
    {
        public UserOperation(Address target, UInt256 nonce, byte[] callData, byte[] initCode, UInt256 callGas, UInt256 verificationGas, UInt256 preVerificationGas, UInt256 maxFeePerGas, UInt256 maxPriorityFeePerGas, Address paymaster, Signature signature, byte[] paymasterData)
        {
            Target = target;
            Nonce = nonce;
            CallData = callData;
            InitCode = initCode;
            CallGas = callGas;
            VerificationGas = verificationGas;
            PreVerificationGas = preVerificationGas;
            MaxFeePerGas = maxFeePerGas;
            MaxPriorityFeePerGas = maxPriorityFeePerGas;
            Paymaster = paymaster;
            Signature = signature;
            PaymasterData = paymasterData;

            AccessList = UserOperationAccessList.Empty;
            
            Hash = CalculateHash(this);
        }

        public UserOperation() {}

        public UserOperationAbi Abi => new UserOperationAbi
        {
            Target = Target!,
            Nonce = Nonce,
            InitCode = InitCode ?? Bytes.Empty,
            CallData = CallData ?? Bytes.Empty,
            CallGas = CallGas,
            VerificationGas = VerificationGas,
            PreVerificationGas = PreVerificationGas,
            MaxFeePerGas = MaxFeePerGas,
            MaxPriorityFeePerGas = MaxPriorityFeePerGas,
            Paymaster = Paymaster!,
            PaymasterData = PaymasterData ?? Bytes.Empty,
            Signature = Bytes.FromHexString(Signature!.ToString())
        };

        public Keccak? Hash { get; set; }
        public Address? Target { get; set; }
        public UInt256 Nonce { get; set; }
        public byte[]? CallData { get; set; }
        public byte[]? InitCode { get; set; }
        public UInt256 CallGas { get; set; }
        public UInt256 VerificationGas { get; set; }
        public UInt256 PreVerificationGas { get; set; }
        public UInt256 MaxFeePerGas { get; set; }
        public UInt256 MaxPriorityFeePerGas { get; set; }
        public Address? Paymaster { get; set; }
        public Signature? Signature { get; set; }
        public byte[]? PaymasterData { get; set; }
        public UserOperationAccessList AccessList { get; set; }
        public int ResimulationCounter { get; set; }
        public bool AccessListTouched { get; set; }
    }

    public struct UserOperationAbi
    {
        public Address Target { get; set; }
        public UInt256 Nonce { get; set; }
        public byte[] InitCode { get; set; }
        public byte[] CallData { get; set; }
        public UInt256 CallGas { get; set; }
        public UInt256 VerificationGas { get; set; }
        public UInt256 PreVerificationGas { get; set; }
        public UInt256 MaxFeePerGas { get; set; }
        public UInt256 MaxPriorityFeePerGas { get; set; }
        public Address Paymaster { get; set; }
        public byte[] PaymasterData { get; set; }
        public byte[] Signature { get; set; }
    }
}
