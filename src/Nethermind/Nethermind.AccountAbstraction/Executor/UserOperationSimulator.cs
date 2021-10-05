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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Abi;
using Nethermind.AccountAbstraction.Data;
using Nethermind.AccountAbstraction.Source;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Contracts.Json;
using Nethermind.Blockchain.Processing;
using Nethermind.Blockchain.Receipts;
using Nethermind.Blockchain.Rewards;
using Nethermind.Blockchain.Tracing;
using Nethermind.Blockchain.Validators;
using Nethermind.Consensus;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Core.Specs;
using Nethermind.Crypto;
using Nethermind.Db;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.Int256;
using Nethermind.Logging;
using Nethermind.State;
using Nethermind.Trie.Pruning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Nethermind.AccountAbstraction.Executor
{
    public class UserOperationSimulator : IUserOperationSimulator
    {
        private readonly IStateProvider _stateProvider;
        private readonly ISigner _signer;
        private readonly IAccountAbstractionConfig _config;
        private readonly Address _singletonAddress;
        private readonly ISpecProvider _specProvider;
        private readonly IBlockTree _blockTree;
        private readonly IReadOnlyDbProvider _dbProvider;
        private readonly IReadOnlyTrieStore _trieStore;
        private readonly ILogManager _logManager;
        private readonly IBlockPreprocessorStep _recoveryStep;

        private AbiDefinition _contract;
        private IAbiEncoder _abiEncoder;

        public UserOperationSimulator(
            IStateProvider stateProvider,
            ISigner signer,
            IAccountAbstractionConfig config,
            Address singletonAddress,
            ISpecProvider specProvider,
            IBlockTree blockTree,
            IDbProvider dbProvider,
            IReadOnlyTrieStore trieStore,
            ILogManager logManager,
            IBlockPreprocessorStep recoveryStep)
        {
            _stateProvider = stateProvider;
            _signer = signer;
            _config = config;
            _singletonAddress = singletonAddress;
            _specProvider = specProvider;
            _blockTree = blockTree;
            _dbProvider = dbProvider.AsReadOnly(false);
            _trieStore = trieStore;
            _logManager = logManager;
            _recoveryStep = recoveryStep;
            
            _abiEncoder = new AbiEncoder();

            using (StreamReader r = new StreamReader("Contracts/EntryPoint.json"))
            {
                string json = r.ReadToEnd();
                JObject obj = JObject.Parse(json);
                
                _contract = LoadContract(obj);
            }
        }

        public Transaction BuildTransactionFromUserOperations(IEnumerable<UserOperation> userOperations, BlockHeader parent, IReleaseSpec spec)
        {
            AbiSignature abiSignature = _contract.Functions["handleOps"].GetCallInfo().Signature;
            
            byte[] computedCallData = _abiEncoder.Encode(
                AbiEncodingStyle.IncludeSignature,
                abiSignature,
                userOperations.Select(op => op.Abi).ToArray(), _signer.Address);
            
            long gaslimit = userOperations.Aggregate((long)0,
                (sum, operation) => sum + (long)operation.VerificationGas + (long)operation.CallGas + 100000); // TODO WHAT CONSTANT

            Transaction transaction = BuildTransaction(gaslimit, computedCallData, _signer.Address, parent, spec, false);
            
            return transaction;
        }

        public Task<bool> Simulate(
            UserOperation userOperation, 
            BlockHeader parent,
            CancellationToken cancellationToken = default, 
            UInt256? timestamp = null)
        {
            IReleaseSpec currentSpec = _specProvider.GetSpec(parent.Number + 1);
            ReadOnlyTxProcessingEnv txProcessingEnv = new(_dbProvider, _trieStore, _blockTree, _specProvider, _logManager);
            ITransactionProcessor transactionProcessor = txProcessingEnv.Build(_stateProvider.StateRoot);
            
            Transaction simulateWalletValidationTransaction = BuildSimulateWalletValidationTransaction(userOperation, parent, currentSpec);
            (bool walletValidationSuccess, UInt256 gasUsedByPayForSelfOp, UserOperationAccessList walletValidationAccessList) =
                SimulateWalletValidation(simulateWalletValidationTransaction, parent, transactionProcessor);

            if (!walletValidationSuccess
                || userOperation.VerificationGas < gasUsedByPayForSelfOp)
            {
                return Task.FromResult(false);
            }
            
            if (userOperation.Paymaster == Address.Zero)
            {
                if (userOperation.AccessListTouched)
                {
                    if (!UserOperationAccessList.AccessListContains(userOperation.AccessList.Data,
                        walletValidationAccessList.Data))
                    {
                        return Task.FromResult(false);
                    }
                }
                else
                {
                    userOperation.AccessList = walletValidationAccessList;
                }
                return Task.FromResult(true);
            }
            
            Transaction simulatePaymasterValidationTransaction = BuildSimulatePaymasterValidationTransaction(userOperation, gasUsedByPayForSelfOp, parent, currentSpec);
            (bool paymasterValidationSuccess, UInt256 gasUsedByPayForOp, UserOperationAccessList paymasterValidationAccessList) = 
                SimulatePaymasterValidation(simulatePaymasterValidationTransaction, parent, transactionProcessor);

            if (!paymasterValidationSuccess
                || userOperation.VerificationGas < gasUsedByPayForSelfOp + gasUsedByPayForOp)
            {
                return Task.FromResult(false);
            }
            
            if (userOperation.AccessListTouched)
            {
                if (!UserOperationAccessList.AccessListContains(userOperation.AccessList.Data,
                    paymasterValidationAccessList.Data))
                {
                    return Task.FromResult(false);
                }
            }
            else
            {
                UserOperationAccessList.CombineAccessLists(userOperation.AccessList.Data, walletValidationAccessList.Data);
            }

            return Task.FromResult(true);
        }
        
        private (bool success, UInt256 gasUsed, UserOperationAccessList accessList) SimulateWalletValidation(Transaction transaction, BlockHeader parent, ITransactionProcessor transactionProcessor)
        {
            UserOperationBlockTracer blockTracer = SimulateValidation(transaction, parent, transactionProcessor);

            if (!blockTracer.Success) return (false, UInt256.Zero, UserOperationAccessList.Empty);
            
            // uint gasUsedByPayForSelfOp
            object[] result = _abiEncoder.Decode(
                AbiEncodingStyle.None,
                _contract.Functions["simulateWalletValidation"].GetReturnInfo().Signature, 
                blockTracer.Output);

            bool success = blockTracer.Success;
            UInt256 gasUsed = (UInt256) result[0];
            UserOperationAccessList userOperationAccessList = new UserOperationAccessList(blockTracer.AccessedStorage);

            return (success, gasUsed, userOperationAccessList);
        }

        private (bool success, UInt256 gasUsed, UserOperationAccessList accessList) SimulatePaymasterValidation(Transaction transaction, BlockHeader parent, ITransactionProcessor transactionProcessor)
        {
            UserOperationBlockTracer blockTracer = SimulateValidation(transaction, parent, transactionProcessor);

            if (!blockTracer.Success) return (false, UInt256.Zero, UserOperationAccessList.Empty);

            // bytes context, uint gasUsedByPayForOp
            object[] result = _abiEncoder.Decode(
                AbiEncodingStyle.None,
                _contract.Functions["simulatePaymasterValidation"].GetReturnInfo().Signature, 
                blockTracer.Output);

            bool success = blockTracer.Success;
            UInt256 gasUsed = (UInt256) result[1];
            UserOperationAccessList userOperationAccessList = new UserOperationAccessList(blockTracer.AccessedStorage);

            return (success, gasUsed, userOperationAccessList);
        }

        private UserOperationBlockTracer SimulateValidation(Transaction transaction, BlockHeader parent, ITransactionProcessor transactionProcessor)
        {
            UserOperationBlockTracer blockTracer = CreateBlockTracer(parent);
            ITxTracer txTracer = blockTracer.StartNewTxTrace(transaction);
            transactionProcessor.CallAndRestore(transaction, parent, txTracer);
            blockTracer.EndTxTrace();

            return blockTracer;
        }

        private Transaction BuildSimulateWalletValidationTransaction(
            UserOperation userOperation, 
            BlockHeader parent,
            IReleaseSpec spec)
        {
            AbiSignature abiSignature = _contract.Functions["simulateWalletValidation"].GetCallInfo().Signature;
            UserOperationAbi userOperationAbi = userOperation.Abi;
            
            byte[] computedCallData = _abiEncoder.Encode(
                AbiEncodingStyle.IncludeSignature,
                abiSignature,
                userOperationAbi);

            Transaction transaction = BuildTransaction((long)userOperation.VerificationGas, computedCallData, Address.Zero, parent, spec, true);
            
            return transaction;
        }
        
        private Transaction BuildSimulatePaymasterValidationTransaction(
            UserOperation userOperation,
            UInt256 gasUsedByPayForSelfOp,
            BlockHeader parent,
            IReleaseSpec spec)
        {
            AbiSignature abiSignature = _contract.Functions["simulatePaymasterValidation"].GetCallInfo().Signature;
            UserOperationAbi userOperationAbi = userOperation.Abi;
            
            byte[] computedCallData = _abiEncoder.Encode(
                AbiEncodingStyle.IncludeSignature,
                abiSignature,
                userOperationAbi, gasUsedByPayForSelfOp);

            Transaction transaction = BuildTransaction((long)userOperation.VerificationGas, computedCallData, Address.Zero, parent, spec, true);
            
            return transaction;
        }

        private Transaction BuildTransaction(long gaslimit, byte[] callData, Address sender, BlockHeader parent, IReleaseSpec spec, bool systemTransaction)
        {
            Transaction transaction = systemTransaction ? new SystemTransaction() : new Transaction();

            transaction.GasPrice = 0;
            transaction.GasLimit = gaslimit;
            transaction.To = _singletonAddress;
            transaction.ChainId = _specProvider.ChainId;
            transaction.Nonce = _stateProvider.GetNonce(_signer.Address);
            transaction.Value = 0;
            transaction.Data = callData;
            transaction.Type = TxType.EIP1559;
            transaction.DecodedMaxFeePerGas = BaseFeeCalculator.Calculate(parent, spec);
            transaction.SenderAddress = sender;
            transaction.Hash = transaction.CalculateHash();

            return transaction;
        }

        private UserOperationBlockTracer CreateBlockTracer(BlockHeader parent) =>
            new(parent.GasLimit, _signer.Address, _stateProvider);
        
        private AbiDefinition LoadContract(JObject obj)
        {
            AbiDefinitionParser parser = new();
            parser.RegisterAbiTypeFactory(new AbiTuple<UserOperationAbi>());
            AbiDefinition contract = parser.Parse(obj["abi"]!.ToString());
            AbiTuple<UserOperationAbi> userOperationAbi = new();
            return contract;
        }
    }
}
