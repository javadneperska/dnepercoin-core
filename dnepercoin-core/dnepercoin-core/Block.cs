using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace dnepercoin_core
{
    public class Block
    {
        // header
        public byte[] previousBlockHash;
        public byte[] transactionHash;
        public byte[] rewardTarget;
        public ulong nonce;
        public uint timestamp;
        public uint difficulty;
        // content
        public ulong numTransactions;
        public List<Transaction> transactions;

        public byte[] transactionData;
        public byte[] originalData;

        public Block()
        {
            previousBlockHash = new byte[32];
            transactionHash = new byte[32];
            rewardTarget = new byte[20];
            transactions = new List<Transaction>();
        }
        
        public static Block FromBytes(byte[] data)
        {
            var block = new Block();

            block.originalData = data;

            Array.Copy(data, 0, block.rewardTarget, 0, 20);
            block.nonce = BitConverter.ToUInt64(data, 20);
            block.timestamp = BitConverter.ToUInt32(data, 28);
            block.difficulty = BitConverter.ToUInt32(data, 32);
            Array.Copy(data, 36, block.previousBlockHash, 0, 32);
            Array.Copy(data, 68, block.transactionHash, 0, 32);

            if (block.difficulty < Program.GlobalDifficulty)
                return null;

            if (Program.LastBlockHash != null && !block.previousBlockHash.SequenceEqual(Program.LastBlockHash))
                return null;

            byte[] blockHash = null;
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(data, 0, 100);
                
                for (int i = 0; i < block.difficulty; i++)
                {
                    int b = 0, j = i;
                    while (j > 7)
                    {
                        b++;
                        j -= 8;
                    }

                    if ((hash[b] & (1 << (7 - j))) != 0)
                        return null;
                }

                blockHash = hash;

                byte[] transactionHash = sha256.ComputeHash(data, 100, data.Length - 100);

                if (!block.transactionHash.SequenceEqual(transactionHash))
                    return null;
            }

            block.numTransactions = BitConverter.ToUInt64(data, 100);
            int index = 108;
            for (ulong i = 0; i < block.numTransactions; i++)
            {
                ushort len = BitConverter.ToUInt16(data, index);
                byte[] transactionData = new byte[len];
                Array.Copy(data, index + 2, transactionData, 0, len);
                index += 2 + len;
                var transaction = Transaction.FromBytes(transactionData);
                if (transaction == null)
                    continue;
                block.transactions.Add(transaction);
            }

            if (!Program.Balances.ContainsKey(block.rewardTarget))
                Program.Balances[block.rewardTarget] = 0;
            Program.Balances[block.rewardTarget] += 10;

            Program.LastBlockHash = blockHash;

            return block;
        }

        public void SetupTransactionHash()
        {
            List<byte> transactionBytes = new List<byte>();
            numTransactions = (ulong)transactions.Count;
            transactionBytes.AddRange(BitConverter.GetBytes(numTransactions));
            foreach (var tx in transactions)
            {
                if (tx.originalData == null)
                    tx.GetBytesTotal();
                transactionBytes.AddRange(BitConverter.GetBytes((ushort)tx.originalData.Length));
                transactionBytes.AddRange(tx.originalData);
            }
            transactionData = transactionBytes.ToArray();
            using (SHA256 sha256 = SHA256.Create())
            {
                transactionHash = sha256.ComputeHash(transactionData);
            }
        }

        public byte[] CreateHeader()
        {
            List<byte> header = new List<byte>();
            header.AddRange(rewardTarget);
            header.AddRange(BitConverter.GetBytes(nonce));
            header.AddRange(BitConverter.GetBytes(timestamp));
            header.AddRange(BitConverter.GetBytes(difficulty));
            header.AddRange(previousBlockHash);
            header.AddRange(transactionHash);
            return header.ToArray();
        }
    }
}
