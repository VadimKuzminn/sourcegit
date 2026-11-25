using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.Models
{
    public record SSHKeyType(string Name, string Description, string Cmdline)
    {
        public static readonly List<SSHKeyType> Supported = [
            new SSHKeyType("ED25519", "Recommended", "-t ed25519"),
            new SSHKeyType("RSA", "4096 Bits", "-t rsa -b 4096"),
        ];
    }

    public class SSHKeyPair : ObservableObject
    {
        public string PrivateKeyPath { get; set; }
        public string PublicKeyPath { get; set; }
        public string RawPublicKey { get; set; } = string.Empty;
        public string Fingerprint { get; set; } = "--- (invalid)";
        public string Name => Path.GetFileName(PrivateKeyPath);

        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }

        public SSHKeyPair(string privateKey, string publicKey)
        {
            PrivateKeyPath = privateKey;
            PublicKeyPath = publicKey;

            try
            {
                if (File.Exists(publicKey))
                    RawPublicKey = File.ReadAllText(publicKey).Trim();
            }
            catch
            {
                // Ignore errors and keep the public key empty
            }

            var parts = RawPublicKey.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return;

            try
            {
                var blob = Convert.FromBase64String(parts[1]);
                Fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(blob)).TrimEnd('=');
            }
            catch
            {
                // Ignore errors and keep the fingerprint as invalid
            }
        }

        private bool _isChecked = false;
    }
}
