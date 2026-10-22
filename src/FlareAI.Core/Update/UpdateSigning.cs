using System;
using System.Security.Cryptography;

namespace FlareAI.Core.Update
{
    /// <summary>
    /// Proves a release came from House Style, not just from GitHub: each release carries <c>checksums.txt.sig</c>, an
    /// RSA-SHA256 (PKCS#1 v1.5) signature of <c>checksums.txt</c> by the release key, which only the gated release job can
    /// use. The public half is built in here, so a stolen GitHub token can publish a release but not one that installs.
    /// </summary>
    public static class UpdateSigning
    {
        public const string SignatureAssetName = "checksums.txt.sig";

        /// <summary>The release key's public half (3072-bit RSA). Rotating it takes a release signed with the old key.</summary>
        public const string PublicKeyXml =
            "<RSAKeyValue><Modulus>2AMHL1rI9LOjAKiihlzUbsf14umENKaJMSZ6p/4GpljMR62ZaJm/ui13Iz6oHd4N42skucwhkr/zrUixmukjez+tLJTov2gVPkNH7QrQqUqhSr3ou1yEx1m5hW+AVtYVqPhZ6p5e4a1ZBkOtImAaRWznh9DXWXQY5ArlZAEbCJOFChlVVgOPbgK15IW6e4Yr2I26WNMTumEQpSVu1cnlscGYOb0u53LNBz2HQMkg4LOVzqaszCgh54/pe9/QcOath54M5zv/j+6/voWjRYqdXy6cvLpKIxXUmHKHl50ZELOiv/Mw/Sa4bZAo/ibjV0Kn2O1GVBDwI37OThbsNEmvgI2EVMIRAJreqWqORvF4V3MtBcw4vIKI8zE0Pa4wdMsooc9798CzaEKLrP+2eLaIgd0m8zKShX9qq+90a9j+3UIXTV2o39g+370ML7wgE04wo/n7/xlj9C1UQSQNm+AQVJcaOY4RVOpsSCrVLZ/vgXBH60ag7N/9WDXqpP1ZW8st</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        /// <summary>True when <paramref name="signatureBase64"/> is the key's signature of <paramref name="data"/>.</summary>
        public static bool Verify(byte[] data, string signatureBase64, string publicKeyXml)
        {
            if (data == null || string.IsNullOrEmpty(signatureBase64) || string.IsNullOrEmpty(publicKeyXml)) return false;
            try
            {
                var sig = Convert.FromBase64String(signatureBase64.Trim());
                // PROV_RSA_AES: the provider that knows SHA-256 on every Windows .NET 4 runs on.
                using (var rsa = new RSACryptoServiceProvider(new CspParameters(24)) { PersistKeyInCsp = false })
                {
                    rsa.FromXmlString(publicKeyXml);
                    return rsa.VerifyData(data, CryptoConfig.MapNameToOID("SHA256"), sig);
                }
            }
            catch (Exception ex) when (ex is FormatException || ex is CryptographicException) { return false; }
        }
    }
}
