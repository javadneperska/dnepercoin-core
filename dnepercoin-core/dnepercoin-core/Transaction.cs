using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace dnepercoin_core
{
    public class Transaction
    {
        public byte[] target;
        public byte[] source;
        public double amount;
        public uint timestamp;
        public byte[] signature;

        public byte[] originalData;

        public Transaction()
        {
            target = new byte[20];
            source = new byte[72];
        }

        public static Transaction FromBytes(byte[] data, bool partOfBlock = true)
        {
            var transaction = new Transaction();

            transaction.originalData = data;

            Array.Copy(data, 0, transaction.source, 0, 72);
            Array.Copy(data, 72, transaction.target, 0, 20);
            transaction.amount = BitConverter.ToDouble(data, 92);
            transaction.timestamp = BitConverter.ToUInt32(data, 100);
            transaction.signature = new byte[data.Length - 104];
            Array.Copy(data, 104, transaction.signature, 0, data.Length - 104);

            byte[] pubKeyHash = new byte[20];
            using (SHA1 sha1 = SHA1.Create())
            {
                pubKeyHash = sha1.ComputeHash(transaction.source);
            }

            if (transaction.amount > Program.Balances[pubKeyHash])
            {
                Console.WriteLine("Bad amount - too much");
                return null;
            }

            if (transaction.amount <= 0)
            {
                Console.WriteLine("Bad amount - negative");
                return null;
            }

            var publicKey = CngKey.Import(transaction.source, CngKeyBlobFormat.EccPublicBlob);
            var publicKeyChecker = new ECDsaCng(publicKey);
            if (!publicKeyChecker.VerifyData(data, 0, 104, transaction.signature, HashAlgorithmName.SHA256))
            {
                Console.WriteLine("Bad amount - bad signature");
                return null;
            }

            if (partOfBlock)
            {
                    Program.Balances[pubKeyHash] -= transaction.amount;
                    if (!Program.Balances.ContainsKey(transaction.target))
                        Program.Balances[transaction.target] = 0;
                    Program.Balances[transaction.target] += transaction.amount;
            }

            return transaction;
        }

        public byte[] GetBytesToSign()
        {
            List<byte> output = new List<byte>();
            output.AddRange(source);
            output.AddRange(target);
            output.AddRange(BitConverter.GetBytes(amount));
            output.AddRange(BitConverter.GetBytes(timestamp));
            return output.ToArray();
        }

        public byte[] GetBytesTotal()
        {
            List<byte> output = new List<byte>();
            output.AddRange(source);
            output.AddRange(target);
            output.AddRange(BitConverter.GetBytes(amount));
            output.AddRange(BitConverter.GetBytes(timestamp));
            output.AddRange(signature);
            originalData = output.ToArray();
            return originalData;
        }
    }
}
